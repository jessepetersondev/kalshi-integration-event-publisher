using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Contracts.Diagnostics;
using Kalshi.Integration.Executor.Execution;
using Kalshi.Integration.Executor.Messaging;
using Kalshi.Integration.Executor.Persistence;
using Kalshi.Integration.Executor.Persistence.Entities;
using Kalshi.Integration.Infrastructure.Integrations.Kalshi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Executor.Handlers;

public sealed class OrderCreatedHandler(
    ExecutorDbContext dbContext,
    IKalshiApiClient kalshiApiClient,
    RabbitMqResultEventPublisher resultEventPublisher,
    RabbitMqInboundEventPublisher inboundEventPublisher,
    DeadLetterEventPublisher deadLetterEventPublisher,
    ExecutionReliabilityPolicy reliabilityPolicy,
    IOptions<KalshiApiOptions> options,
    ILogger<OrderCreatedHandler> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ExecutorDbContext _dbContext = dbContext;
    private readonly IKalshiApiClient _kalshiApiClient = kalshiApiClient;
    private readonly RabbitMqResultEventPublisher _resultEventPublisher = resultEventPublisher;
    private readonly RabbitMqInboundEventPublisher _inboundEventPublisher = inboundEventPublisher;
    private readonly DeadLetterEventPublisher _deadLetterEventPublisher = deadLetterEventPublisher;
    private readonly ExecutionReliabilityPolicy _reliabilityPolicy = reliabilityPolicy;
    private readonly KalshiApiOptions _options = options.Value;
    private readonly ILogger<OrderCreatedHandler> _logger = logger;
    private readonly string _processorId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public async Task HandleAsync(ApplicationEventEnvelope envelope, CancellationToken cancellationToken = default)
    {
        OrderCommand command = ParseOrderCommand(envelope);
        ExecutionAcquisition acquisition = await AcquireExecutionAsync(envelope, command, cancellationToken);
        ExecutionRecordEntity execution = acquisition.Execution;

        try
        {
            if (!acquisition.ShouldProcess)
            {
                await EnsureTerminalResultQueuedAsync(envelope, command, execution, cancellationToken);
                return;
            }

            if (string.Equals(command.ActionType, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                await HandleCancelAsync(envelope, command, execution, cancellationToken);
                return;
            }

            JsonObject? recovered = await TryRecoverFromPersistedExecutionStateAsync(command, execution, cancellationToken)
                ?? await TryRecoverExistingOrderAsync(command, cancellationToken);
            if (recovered is null)
            {
                recovered = ExtractOrderNode(await _kalshiApiClient.PlaceOrderAsync(BuildPlaceOrderPayload(command), cancellationToken));
            }
            else
            {
                RecordDuplicateGuardHit("recovered_existing_order");
            }

            await PersistSuccessfulExecutionAsync(envelope, command, execution.Id, recovered, cancellationToken);
        }
        catch (Exception exception) when (!_reliabilityPolicy.IsRetryable(exception))
        {
            await PersistFailureAsync(envelope, command, execution.Id, exception, deadLetter: false, cancellationToken);
        }
    }

    private async Task<ExecutionAcquisition> AcquireExecutionAsync(ApplicationEventEnvelope envelope, OrderCommand command, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                return await AcquireExecutionCoreAsync(envelope, command, cancellationToken);
            }
            catch (DbUpdateException exception) when (attempt == 0 && IsUniqueConstraintViolation(exception))
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        return await AcquireExecutionCoreAsync(envelope, command, cancellationToken);
    }

    private async Task<ExecutionAcquisition> AcquireExecutionCoreAsync(ApplicationEventEnvelope envelope, OrderCommand command, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        ExecutorInboundMessageEntity? inbound = await _dbContext.InboundMessages.SingleOrDefaultAsync(x => x.Id == envelope.Id, cancellationToken);
        if (inbound?.HandledAt.HasValue == true)
        {
            ExecutionRecordEntity existingExecution = await FindExecutionAsync(command, cancellationToken)
                ?? throw new KeyNotFoundException($"Execution for publisher order '{command.PublisherOrderId}' was not found.");

            await transaction.CommitAsync(cancellationToken);
            return new ExecutionAcquisition(existingExecution, ShouldProcess: false);
        }

        if (inbound is null)
        {
            inbound = new ExecutorInboundMessageEntity
            {
                Id = envelope.Id,
                Name = envelope.Name,
                ResourceId = envelope.ResourceId,
                CorrelationId = envelope.CorrelationId,
                IdempotencyKey = envelope.IdempotencyKey,
                PayloadJson = JsonSerializer.Serialize(envelope, SerializerOptions),
                OccurredAt = envelope.OccurredAt,
                CreatedAt = now,
            };

            _dbContext.InboundMessages.Add(inbound);
        }

        inbound.ReceiveAttemptCount++;
        inbound.LastReceivedAt = now;

        ExecutionRecordEntity? execution = await FindExecutionAsync(command, cancellationToken);

        if (execution is null)
        {
            execution = new ExecutionRecordEntity
            {
                Id = Guid.NewGuid(),
                PublisherOrderId = command.PublisherOrderId,
                TradeIntentId = command.TradeIntentId,
                CommandEventId = envelope.Id,
                LastSourceEventId = envelope.Id,
                Ticker = command.Ticker,
                ActionType = command.ActionType,
                Side = command.Side,
                Quantity = command.Quantity,
                LimitPrice = command.LimitPrice,
                CorrelationId = command.CorrelationId,
                ClientOrderId = command.ClientOrderId,
                Status = "received",
                AttemptCount = 0,
                CreatedAt = now,
                UpdatedAt = now,
            };

            _dbContext.ExecutionRecords.Add(execution);
        }

        if (execution.TerminalResultQueuedAt.HasValue)
        {
            inbound.HandledAt ??= now;
            inbound.LastError = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ExecutionAcquisition(execution, ShouldProcess: false);
        }

        if (execution.LeaseExpiresAt.HasValue
            && execution.LeaseExpiresAt > now
            && !string.Equals(execution.LeaseOwner, _processorId, StringComparison.Ordinal))
        {
            throw new RetryableExecutionException($"Execution lease for publisher order {command.PublisherOrderId} is currently held by another worker.");
        }

        execution.AttemptCount++;
        execution.LastSourceEventId = envelope.Id;
        execution.LeaseOwner = _processorId;
        execution.LeaseExpiresAt = now.AddSeconds(30);
        execution.UpdatedAt = now;

        _inboundEventPublisher.QueueAsync(_dbContext, execution.Id, CreateInboundEvent(envelope, command), now);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ExecutionAcquisition(execution, ShouldProcess: true);
    }

    private async Task HandleCancelAsync(ApplicationEventEnvelope envelope, OrderCommand command, ExecutionRecordEntity execution, CancellationToken cancellationToken)
    {
        string? externalOrderId = !string.IsNullOrWhiteSpace(command.TargetExternalOrderId)
            ? command.TargetExternalOrderId
            : await TryResolveTargetExternalOrderIdAsync(command, cancellationToken);

        if (string.IsNullOrWhiteSpace(externalOrderId))
        {
            throw new InvalidOperationException("Cancel command is missing a target external order id.");
        }

        JsonObject canceled = ExtractOrderNode(await _kalshiApiClient.CancelOrderAsync(externalOrderId, _options.Subaccount, cancellationToken));
        await PersistSuccessfulExecutionAsync(envelope, command, execution.Id, canceled, cancellationToken, overrideStatus: "canceled");
    }

    private async Task<JsonObject?> TryRecoverExistingOrderAsync(OrderCommand command, CancellationToken cancellationToken)
    {
        JsonNode payload = await _kalshiApiClient.GetOrdersAsync(command.Ticker, _options.Subaccount, cancellationToken);
        if (payload["orders"] is not JsonArray orders)
        {
            return null;
        }

        foreach (JsonObject item in orders.OfType<JsonObject>())
        {
            if (string.Equals(item["client_order_id"]?.GetValue<string>(), command.ClientOrderId, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private async Task<JsonObject?> TryRecoverFromPersistedExecutionStateAsync(
        OrderCommand command,
        ExecutionRecordEntity execution,
        CancellationToken cancellationToken)
    {
        string? externalOrderId = execution.ExternalOrderId;

        if (string.IsNullOrWhiteSpace(externalOrderId))
        {
            externalOrderId = await _dbContext.ExternalOrderMappings
                .AsNoTracking()
                .Where(x => x.PublisherOrderId == command.PublisherOrderId || x.ClientOrderId == command.ClientOrderId)
                .Select(x => x.ExternalOrderId)
                .FirstOrDefaultAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(externalOrderId))
        {
            return null;
        }

        JsonObject recovered = ExtractOrderNode(await _kalshiApiClient.GetOrderAsync(externalOrderId, _options.Subaccount, cancellationToken));
        return HasOrderIdentity(recovered) ? recovered : null;
    }

    private async Task<string?> TryResolveTargetExternalOrderIdAsync(OrderCommand command, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(command.TargetClientOrderId))
        {
            JsonNode existing = await _kalshiApiClient.GetOrdersAsync(command.Ticker, _options.Subaccount, cancellationToken);
            if (existing["orders"] is JsonArray orders)
            {
                foreach (JsonObject item in orders.OfType<JsonObject>())
                {
                    if (string.Equals(item["client_order_id"]?.GetValue<string>(), command.TargetClientOrderId, StringComparison.Ordinal))
                    {
                        return item["order_id"]?.GetValue<string>();
                    }
                }
            }
        }

        ExternalOrderMappingEntity? mapping = await _dbContext.ExternalOrderMappings.AsNoTracking()
            .SingleOrDefaultAsync(x => x.PublisherOrderId == command.TargetPublisherOrderId, cancellationToken);
        return mapping?.ExternalOrderId;
    }

    private async Task EnsureTerminalResultQueuedAsync(
        ApplicationEventEnvelope envelope,
        OrderCommand command,
        ExecutionRecordEntity execution,
        CancellationToken cancellationToken)
    {
        if (execution.TerminalResultQueuedAt.HasValue)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(execution.ExternalOrderId))
        {
            JsonObject? recovered = await TryRecoverFromPersistedExecutionStateAsync(command, execution, cancellationToken)
                ?? await TryRecoverExistingOrderAsync(command, cancellationToken);

            if (recovered is not null)
            {
                RecordDuplicateGuardHit("replayed_terminal_success");
                await PersistSuccessfulExecutionAsync(envelope, command, execution.Id, recovered, cancellationToken);
                return;
            }
        }

        if (string.Equals(execution.LastResultEventName, "order.dead_lettered", StringComparison.OrdinalIgnoreCase))
        {
            RecordDuplicateGuardHit("replayed_terminal_dead_letter");
            await PersistFailureAsync(
                envelope,
                command,
                execution.Id,
                new InvalidOperationException(execution.LastError ?? "Execution was dead-lettered."),
                deadLetter: true,
                cancellationToken);
            return;
        }

        if (string.Equals(execution.LastResultEventName, "order.execution_failed", StringComparison.OrdinalIgnoreCase))
        {
            RecordDuplicateGuardHit("replayed_terminal_failure");
            await PersistFailureAsync(
                envelope,
                command,
                execution.Id,
                new InvalidOperationException(execution.LastError ?? "Execution failed."),
                deadLetter: false,
                cancellationToken);
        }
    }

    private async Task PersistSuccessfulExecutionAsync(
        ApplicationEventEnvelope envelope,
        OrderCommand command,
        Guid executionRecordId,
        JsonObject orderNode,
        CancellationToken cancellationToken,
        string? overrideStatus = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        ExecutionRecordEntity execution = await _dbContext.ExecutionRecords.SingleAsync(x => x.Id == executionRecordId, cancellationToken);
        ExecutorInboundMessageEntity inbound = await _dbContext.InboundMessages.SingleAsync(x => x.Id == envelope.Id, cancellationToken);

        string? externalOrderId = orderNode["order_id"]?.GetValue<string>();
        string clientOrderId = orderNode["client_order_id"]?.GetValue<string>() ?? command.ClientOrderId;
        string orderStatus = overrideStatus ?? orderNode["status"]?.GetValue<string>() ?? "accepted";
        int filledQuantity = ParseInteger(orderNode["fill_count_fp"]) ?? ParseInteger(orderNode["filled_quantity"]) ?? 0;

        execution.ExternalOrderId = externalOrderId;
        execution.Status = NormalizeStatus(orderStatus);
        execution.LastError = null;
        execution.LastResultEventName = "order.execution_succeeded";
        execution.TerminalResultQueuedAt = now;
        execution.LeaseOwner = null;
        execution.LeaseExpiresAt = null;
        execution.UpdatedAt = now;

        if (!string.IsNullOrWhiteSpace(externalOrderId))
        {
            ExternalOrderMappingEntity? mapping = await _dbContext.ExternalOrderMappings.SingleOrDefaultAsync(x => x.PublisherOrderId == command.PublisherOrderId, cancellationToken);
            if (mapping is null)
            {
                _dbContext.ExternalOrderMappings.Add(new ExternalOrderMappingEntity
                {
                    Id = Guid.NewGuid(),
                    ExecutionRecordId = execution.Id,
                    PublisherOrderId = command.PublisherOrderId,
                    ClientOrderId = clientOrderId,
                    ExternalOrderId = externalOrderId,
                    CreatedAt = now,
                });
            }
        }

        _resultEventPublisher.QueueAsync(
            _dbContext,
            execution.Id,
            CreateSuccessResultEvent(envelope, command, externalOrderId, clientOrderId, orderStatus, filledQuantity),
            now);

        inbound.HandledAt = now;
        inbound.LastError = null;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task PersistFailureAsync(
        ApplicationEventEnvelope envelope,
        OrderCommand command,
        Guid executionRecordId,
        Exception exception,
        bool deadLetter,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        ExecutionRecordEntity execution = await _dbContext.ExecutionRecords.SingleAsync(x => x.Id == executionRecordId, cancellationToken);
        ExecutorInboundMessageEntity inbound = await _dbContext.InboundMessages.SingleAsync(x => x.Id == envelope.Id, cancellationToken);

        execution.Status = deadLetter ? "dead-lettered" : "failed";
        execution.LastError = exception.Message;
        execution.LastResultEventName = deadLetter ? "order.dead_lettered" : "order.execution_failed";
        execution.TerminalResultQueuedAt = now;
        execution.LeaseOwner = null;
        execution.LeaseExpiresAt = null;
        execution.UpdatedAt = now;

        ApplicationEventEnvelope failureEvent = deadLetter
            ? CreateDeadLetterEvent(envelope, command, exception)
            : CreateFailureResultEvent(envelope, command, exception);

        if (deadLetter)
        {
            _deadLetterEventPublisher.QueueAsync(_dbContext, execution.Id, failureEvent, now);
        }
        else
        {
            _resultEventPublisher.QueueAsync(_dbContext, execution.Id, failureEvent, now);
        }

        inbound.HandledAt = now;
        inbound.LastError = exception.Message;

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private JsonObject BuildPlaceOrderPayload(OrderCommand command)
    {
        JsonObject payload = new()
        {
            ["ticker"] = command.Ticker,
            ["client_order_id"] = command.ClientOrderId,
            ["type"] = "limit",
            ["action"] = string.Equals(command.ActionType, "exit", StringComparison.OrdinalIgnoreCase) ? "sell" : "buy",
            ["side"] = command.Side,
            ["count"] = command.Quantity,
            ["subaccount"] = command.Subaccount == 0 ? _options.Subaccount : command.Subaccount,
        };

        if (string.Equals(command.Side, "yes", StringComparison.OrdinalIgnoreCase))
        {
            payload["yes_price_dollars"] = command.LimitPrice?.ToString("0.0000", CultureInfo.InvariantCulture);
        }
        else
        {
            payload["no_price_dollars"] = command.LimitPrice?.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        return payload;
    }

    private static JsonObject ExtractOrderNode(JsonNode payload)
        => payload["order"] as JsonObject ?? payload as JsonObject ?? [];

    private static bool HasOrderIdentity(JsonObject orderNode)
        => !string.IsNullOrWhiteSpace(orderNode["order_id"]?.GetValue<string>());

    private static int? ParseInteger(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        return node switch
        {
            JsonValue value when value.TryGetValue<int>(out int intValue) => intValue,
            JsonValue value when value.TryGetValue<string>(out string? stringValue) && decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal decimalValue) => (int)decimal.Round(decimalValue, 0, MidpointRounding.AwayFromZero),
            _ => null,
        };
    }

    private static string NormalizeStatus(string value)
        => string.IsNullOrWhiteSpace(value) ? "accepted" : value.Trim().ToLowerInvariant();

    private static ApplicationEventEnvelope CreateInboundEvent(ApplicationEventEnvelope source, OrderCommand command)
        => new(
            CreateDeterministicGuid(source.Id, "executor-inbound"),
            "executor",
            "executor.order.received",
            command.PublisherOrderId.ToString(),
            source.CorrelationId,
            source.IdempotencyKey,
            new Dictionary<string, string?>
            {
                ["publisherOrderId"] = command.PublisherOrderId.ToString(),
                ["clientOrderId"] = command.ClientOrderId,
                ["ticker"] = command.Ticker,
                ["actionType"] = command.ActionType,
            },
            DateTimeOffset.UtcNow);

    private static ApplicationEventEnvelope CreateSuccessResultEvent(
        ApplicationEventEnvelope source,
        OrderCommand command,
        string? externalOrderId,
        string clientOrderId,
        string orderStatus,
        int filledQuantity)
        => new(
            CreateDeterministicGuid(source.Id, "executor-success"),
            "execution",
            "order.execution_succeeded",
            command.PublisherOrderId.ToString(),
            source.CorrelationId,
            source.IdempotencyKey,
            new Dictionary<string, string?>
            {
                ["publisherOrderId"] = command.PublisherOrderId.ToString(),
                ["clientOrderId"] = clientOrderId,
                ["externalOrderId"] = externalOrderId,
                ["commandEventId"] = source.Id.ToString(),
                ["orderStatus"] = NormalizeStatus(orderStatus),
                ["filledQuantity"] = filledQuantity.ToString(CultureInfo.InvariantCulture),
            },
            DateTimeOffset.UtcNow);

    private static ApplicationEventEnvelope CreateFailureResultEvent(ApplicationEventEnvelope source, OrderCommand command, Exception exception)
        => new(
            CreateDeterministicGuid(source.Id, "executor-failure"),
            "execution",
            "order.execution_failed",
            command.PublisherOrderId.ToString(),
            source.CorrelationId,
            source.IdempotencyKey,
            new Dictionary<string, string?>
            {
                ["publisherOrderId"] = command.PublisherOrderId.ToString(),
                ["clientOrderId"] = command.ClientOrderId,
                ["commandEventId"] = source.Id.ToString(),
                ["errorMessage"] = exception.Message,
            },
            DateTimeOffset.UtcNow);

    private static ApplicationEventEnvelope CreateDeadLetterEvent(ApplicationEventEnvelope source, OrderCommand command, Exception exception)
        => new(
            CreateDeterministicGuid(source.Id, "executor-dead-letter"),
            "execution",
            "order.dead_lettered",
            command.PublisherOrderId.ToString(),
            source.CorrelationId,
            source.IdempotencyKey,
            new Dictionary<string, string?>
            {
                ["publisherOrderId"] = command.PublisherOrderId.ToString(),
                ["clientOrderId"] = command.ClientOrderId,
                ["commandEventId"] = source.Id.ToString(),
                ["deadLetterQueue"] = "kalshi.integration.executor.dlq",
                ["errorMessage"] = exception.Message,
            },
            DateTimeOffset.UtcNow);

    private static Guid CreateDeterministicGuid(Guid seed, string purpose)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed:N}:{purpose}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes[..16].CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private static OrderCommand ParseOrderCommand(ApplicationEventEnvelope envelope)
    {
        static string Require(IReadOnlyDictionary<string, string?> attributes, string key)
            => attributes.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value.Trim()
                : throw new InvalidOperationException($"Order command is missing required attribute '{key}'.");

        return new OrderCommand(
            PublisherOrderId: Guid.Parse(Require(envelope.Attributes, "publisherOrderId")),
            TradeIntentId: Guid.Parse(Require(envelope.Attributes, "tradeIntentId")),
            Ticker: Require(envelope.Attributes, "ticker"),
            ActionType: Require(envelope.Attributes, "actionType"),
            Side: envelope.Attributes.TryGetValue("side", out string? side) ? side : null,
            Quantity: envelope.Attributes.TryGetValue("quantity", out string? quantity) && int.TryParse(quantity, out int parsedQuantity) ? parsedQuantity : null,
            LimitPrice: envelope.Attributes.TryGetValue("limitPrice", out string? limitPrice) && decimal.TryParse(limitPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedLimitPrice) ? parsedLimitPrice : null,
            CorrelationId: envelope.CorrelationId ?? Require(envelope.Attributes, "clientOrderId"),
            ClientOrderId: Require(envelope.Attributes, "clientOrderId"),
            Subaccount: envelope.Attributes.TryGetValue("subaccount", out string? subaccount) && int.TryParse(subaccount, out int parsedSubaccount) ? parsedSubaccount : 0,
            TargetPublisherOrderId: envelope.Attributes.TryGetValue("targetPublisherOrderId", out string? targetPublisherOrderId) && Guid.TryParse(targetPublisherOrderId, out Guid parsedTargetPublisherOrderId) ? parsedTargetPublisherOrderId : null,
            TargetClientOrderId: envelope.Attributes.TryGetValue("targetClientOrderId", out string? targetClientOrderId) ? targetClientOrderId : null,
            TargetExternalOrderId: envelope.Attributes.TryGetValue("targetExternalOrderId", out string? targetExternalOrderId) ? targetExternalOrderId : null);
    }

    private async Task<ExecutionRecordEntity?> FindExecutionAsync(OrderCommand command, CancellationToken cancellationToken)
    {
        ExecutionRecordEntity? execution = await _dbContext.ExecutionRecords.SingleOrDefaultAsync(x => x.PublisherOrderId == command.PublisherOrderId, cancellationToken)
            ?? await _dbContext.ExecutionRecords.SingleOrDefaultAsync(x => x.ClientOrderId == command.ClientOrderId, cancellationToken);

        if (execution is not null)
        {
            return execution;
        }

        Guid? mappedExecutionId = await _dbContext.ExternalOrderMappings
            .AsNoTracking()
            .Where(x => x.PublisherOrderId == command.PublisherOrderId || x.ClientOrderId == command.ClientOrderId)
            .Select(x => (Guid?)x.ExecutionRecordId)
            .FirstOrDefaultAsync(cancellationToken);

        return mappedExecutionId.HasValue
            ? await _dbContext.ExecutionRecords.SingleOrDefaultAsync(x => x.Id == mappedExecutionId.Value, cancellationToken)
            : null;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
        => exception.InnerException?.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) == true
            || exception.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);

    private static void RecordDuplicateGuardHit(string outcome)
    {
        KalshiTelemetry.DuplicateGuardHitsTotal.Add(
            1,
            new KeyValuePair<string, object?>("component", "executor"),
            new KeyValuePair<string, object?>("outcome", outcome));
    }

    private sealed record OrderCommand(
        Guid PublisherOrderId,
        Guid TradeIntentId,
        string Ticker,
        string ActionType,
        string? Side,
        int? Quantity,
        decimal? LimitPrice,
        string CorrelationId,
        string ClientOrderId,
        int Subaccount,
        Guid? TargetPublisherOrderId,
        string? TargetClientOrderId,
        string? TargetExternalOrderId);

    private sealed record ExecutionAcquisition(ExecutionRecordEntity Execution, bool ShouldProcess);
}
