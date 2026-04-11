using System.Diagnostics;
using System.Text.Json;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Reliability;
using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;
using Kalshi.Integration.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kalshi.Integration.Infrastructure.Persistence;

/// <summary>
/// Implements trading persistence on top of Entity Framework Core.
/// </summary>
public sealed class EfTradingRepository :
    ITradeIntentRepository,
    IOrderRepository,
    IPositionSnapshotRepository,
    IOrderCommandSubmissionStore,
    IPublisherCommandOutboxStore,
    IExecutorResultProjectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Action<ILogger, string, string, double, Exception?> DependencyCallSucceeded =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Information,
            new EventId(1100, nameof(DependencyCallSucceeded)),
            "Dependency call {Dependency} {Operation} succeeded in {ElapsedMs} ms.");

    private static readonly Action<ILogger, string, string, double, Exception?> DependencyCallFailed =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Error,
            new EventId(1101, nameof(DependencyCallFailed)),
            "Dependency call {Dependency} {Operation} failed after {ElapsedMs} ms.");

    private readonly KalshiIntegrationDbContext _dbContext;
    private readonly ILogger<EfTradingRepository> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EfTradingRepository"/> class.
    /// </summary>
    /// <param name="dbContext">The database context used for trading persistence.</param>
    /// <param name="logger">The logger used for persistence diagnostics.</param>
    public EfTradingRepository(KalshiIntegrationDbContext dbContext, ILogger<EfTradingRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task AddTradeIntentAsync(TradeIntent tradeIntent, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("trade-intents.insert", async () =>
        {
            var entity = new TradeIntentEntity
            {
                Id = tradeIntent.Id,
                Ticker = tradeIntent.Ticker,
                Side = tradeIntent.Side?.ToString(),
                Quantity = tradeIntent.Quantity,
                LimitPrice = tradeIntent.LimitPrice,
                StrategyName = tradeIntent.StrategyName,
                CorrelationId = tradeIntent.CorrelationId,
                ActionType = tradeIntent.ActionType.ToString(),
                OriginService = tradeIntent.OriginService,
                DecisionReason = tradeIntent.DecisionReason,
                CommandSchemaVersion = tradeIntent.CommandSchemaVersion,
                TargetPositionTicker = tradeIntent.TargetPositionTicker,
                TargetPositionSide = tradeIntent.TargetPositionSide?.ToString(),
                TargetPublisherOrderId = tradeIntent.TargetPublisherOrderId,
                TargetClientOrderId = tradeIntent.TargetClientOrderId,
                TargetExternalOrderId = tradeIntent.TargetExternalOrderId,
                CreatedAt = tradeIntent.CreatedAt,
            };

            _dbContext.TradeIntents.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task<TradeIntent?> GetTradeIntentAsync(Guid tradeIntentId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("trade-intents.get-by-id", async () =>
        {
            var entity = await _dbContext.TradeIntents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tradeIntentId, cancellationToken);
            return entity is null ? null : MapTradeIntent(entity);
        });

    public Task<TradeIntent?> GetTradeIntentByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("trade-intents.get-by-correlation-id", async () =>
        {
            var entity = await _dbContext.TradeIntents.AsNoTracking().SingleOrDefaultAsync(x => x.CorrelationId == correlationId, cancellationToken);
            return entity is null ? null : MapTradeIntent(entity);
        });

    public Task<TradeIntent?> FindMatchingCancelTradeIntentAsync(
        Guid? targetPublisherOrderId,
        string? targetClientOrderId,
        string? targetExternalOrderId,
        CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("trade-intents.find-matching-cancel", async () =>
        {
            var trimmedClientOrderId = string.IsNullOrWhiteSpace(targetClientOrderId) ? null : targetClientOrderId.Trim();
            var trimmedExternalOrderId = string.IsNullOrWhiteSpace(targetExternalOrderId) ? null : targetExternalOrderId.Trim();

            if (!targetPublisherOrderId.HasValue && trimmedClientOrderId is null && trimmedExternalOrderId is null)
            {
                return null;
            }

            var matchingEntities = await _dbContext.TradeIntents
                .AsNoTracking()
                .Where(x => x.ActionType == TradeIntentActionType.Cancel.ToString())
                .Where(x =>
                    (targetPublisherOrderId.HasValue && x.TargetPublisherOrderId == targetPublisherOrderId.Value)
                    || (trimmedClientOrderId != null && x.TargetClientOrderId == trimmedClientOrderId)
                    || (trimmedExternalOrderId != null && x.TargetExternalOrderId == trimmedExternalOrderId))
                .ToListAsync(cancellationToken);

            var entity = matchingEntities
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();

            return entity is null ? null : MapTradeIntent(entity);
        });

    public Task AddOrderAsync(Order order, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("orders.insert", async () =>
        {
            var entity = new OrderEntity
            {
                Id = order.Id,
                TradeIntentId = order.TradeIntent.Id,
                Status = order.CurrentStatus.ToString(),
                PublishStatus = order.PublishStatus.ToString(),
                LastResultStatus = order.LastResultStatus,
                LastResultMessage = order.LastResultMessage,
                ExternalOrderId = order.ExternalOrderId,
                ClientOrderId = order.ClientOrderId,
                CommandEventId = order.CommandEventId,
                FilledQuantity = order.FilledQuantity,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
            };

            _dbContext.Orders.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task SubmitOrderWithCommandAsync(
        Order order,
        ApplicationEventEnvelope commandEvent,
        PositionSnapshot? initialPositionSnapshot,
        CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("orders.submit-with-command", async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var duplicateTradeIntentOrderExists = await _dbContext.Orders
                .AsNoTracking()
                .AnyAsync(x => x.TradeIntentId == order.TradeIntent.Id, cancellationToken);

            if (duplicateTradeIntentOrderExists)
            {
                throw new DomainException($"Trade intent '{order.TradeIntent.Id}' already has an order.");
            }

            if (!string.IsNullOrWhiteSpace(order.ClientOrderId))
            {
                var duplicateClientOrderExists = await _dbContext.Orders
                    .AsNoTracking()
                    .AnyAsync(x => x.ClientOrderId == order.ClientOrderId, cancellationToken);

                if (duplicateClientOrderExists)
                {
                    throw new DomainException($"Client order id '{order.ClientOrderId}' is already in use.");
                }
            }

            _dbContext.Orders.Add(new OrderEntity
            {
                Id = order.Id,
                TradeIntentId = order.TradeIntent.Id,
                Status = order.CurrentStatus.ToString(),
                PublishStatus = order.PublishStatus.ToString(),
                LastResultStatus = order.LastResultStatus,
                LastResultMessage = order.LastResultMessage,
                ExternalOrderId = order.ExternalOrderId,
                ClientOrderId = order.ClientOrderId,
                CommandEventId = order.CommandEventId,
                FilledQuantity = order.FilledQuantity,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
            });

            _dbContext.OrderEvents.Add(new OrderEventEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Status = order.CurrentStatus.ToString(),
                FilledQuantity = order.FilledQuantity,
                OccurredAt = order.CreatedAt,
            });

            _dbContext.OrderLifecycleEvents.Add(new OrderLifecycleEventEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Stage = "order_created",
                OccurredAt = order.CreatedAt,
            });

            _dbContext.OrderLifecycleEvents.Add(new OrderLifecycleEventEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Stage = "command_outbox_enqueued",
                Details = $"commandEventId={commandEvent.Id}",
                OccurredAt = commandEvent.OccurredAt,
            });

            _dbContext.PublisherOutboxMessages.Add(new PublisherOutboxMessageEntity
            {
                Id = commandEvent.Id,
                AggregateId = order.Id,
                AggregateType = "order",
                PayloadJson = JsonSerializer.Serialize(commandEvent, SerializerOptions),
                Status = OutboxMessageStatus.Pending.ToString(),
                AttemptCount = 0,
                CreatedAt = commandEvent.OccurredAt,
                NextAttemptAt = commandEvent.OccurredAt,
            });

            if (initialPositionSnapshot is not null)
            {
                await UpsertPositionSnapshotEntityAsync(initialPositionSnapshot, cancellationToken);
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
            {
                throw new DomainException($"Trade intent '{order.TradeIntent.Id}' already has an order.");
            }
        });

    public Task UpdateOrderAsync(Order order, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("orders.update", async () =>
        {
            var entity = await _dbContext.Orders.SingleAsync(x => x.Id == order.Id, cancellationToken);
            entity.Status = order.CurrentStatus.ToString();
            entity.PublishStatus = order.PublishStatus.ToString();
            entity.LastResultStatus = order.LastResultStatus;
            entity.LastResultMessage = order.LastResultMessage;
            entity.ExternalOrderId = order.ExternalOrderId;
            entity.ClientOrderId = order.ClientOrderId;
            entity.CommandEventId = order.CommandEventId;
            entity.FilledQuantity = order.FilledQuantity;
            entity.UpdatedAt = order.UpdatedAt;
            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task<Order?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("orders.get-by-id", async () =>
        {
            var orderEntity = await _dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
            if (orderEntity is null)
            {
                return null;
            }

            var tradeIntentEntity = await _dbContext.TradeIntents.AsNoTracking().SingleAsync(x => x.Id == orderEntity.TradeIntentId, cancellationToken);
            return MapOrder(orderEntity, tradeIntentEntity);
        });

    public Task<Order?> GetLatestOrderByTradeIntentIdAsync(Guid tradeIntentId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("orders.get-latest-by-trade-intent-id", async () =>
        {
            var orderEntities = await _dbContext.Orders
                .AsNoTracking()
                .Where(x => x.TradeIntentId == tradeIntentId)
                .ToListAsync(cancellationToken);

            var orderEntity = orderEntities
                .OrderByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.CreatedAt)
                .FirstOrDefault();

            if (orderEntity is null)
            {
                return null;
            }

            var tradeIntentEntity = await _dbContext.TradeIntents.AsNoTracking().SingleAsync(x => x.Id == orderEntity.TradeIntentId, cancellationToken);
            return MapOrder(orderEntity, tradeIntentEntity);
        });

    public Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync<IReadOnlyList<Order>>("orders.list", async () =>
        {
            var orderEntities = await _dbContext.Orders.AsNoTracking().ToListAsync(cancellationToken);
            if (orderEntities.Count == 0)
            {
                return Array.Empty<Order>();
            }

            var tradeIntentIds = orderEntities.Select(x => x.TradeIntentId).Distinct().ToArray();
            var tradeIntentEntities = await _dbContext.TradeIntents
                .AsNoTracking()
                .Where(x => tradeIntentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            return orderEntities
                .OrderByDescending(x => x.UpdatedAt)
                .Select(orderEntity => MapOrder(orderEntity, tradeIntentEntities[orderEntity.TradeIntentId]))
                .ToArray();
        });

    public Task AddOrderEventAsync(ExecutionEvent executionEvent, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("order-events.insert", async () =>
        {
            var entity = new OrderEventEntity
            {
                Id = executionEvent.Id,
                OrderId = executionEvent.OrderId,
                Status = executionEvent.Status.ToString(),
                FilledQuantity = executionEvent.FilledQuantity,
                OccurredAt = executionEvent.OccurredAt,
            };

            _dbContext.OrderEvents.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task<IReadOnlyList<ExecutionEvent>> GetOrderEventsAsync(Guid orderId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync<IReadOnlyList<ExecutionEvent>>("order-events.list-by-order-id", async () =>
        {
            var entities = await _dbContext.OrderEvents.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync(cancellationToken);
            return entities.OrderBy(x => x.OccurredAt).Select(MapExecutionEvent).ToArray();
        });

    public Task AddOrderLifecycleEventAsync(Guid orderId, string stage, string? details, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("order-lifecycle-events.insert", async () =>
        {
            _dbContext.OrderLifecycleEvents.Add(new OrderLifecycleEventEntity
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                Stage = stage,
                Details = details,
                OccurredAt = occurredAt,
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task<IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)>> GetOrderLifecycleEventsAsync(Guid orderId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync<IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)>>("order-lifecycle-events.list-by-order-id", async () =>
        {
            var entities = await _dbContext.OrderLifecycleEvents.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync(cancellationToken);
            return entities
                .OrderBy(x => x.OccurredAt)
                .Select(x => (x.Stage, x.Details, x.OccurredAt))
                .ToArray();
        });

    public Task<bool> TryAddResultEventAsync(Guid resultEventId, Guid? orderId, string name, string? correlationId, string? idempotencyKey, string payloadJson, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("result-events.insert", async () =>
        {
            var exists = await _dbContext.ResultEvents.AsNoTracking().AnyAsync(x => x.Id == resultEventId, cancellationToken);
            if (exists)
            {
                return false;
            }

            _dbContext.ResultEvents.Add(new ResultEventEntity
            {
                Id = resultEventId,
                OrderId = orderId,
                Name = name,
                CorrelationId = correlationId,
                IdempotencyKey = idempotencyKey,
                PayloadJson = payloadJson,
                OccurredAt = occurredAt,
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        });

    public Task<bool> ApplyExecutorResultAsync(
        ApplicationEventEnvelope resultEvent,
        ResultProjectionMutation mutation,
        CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("result-events.apply", async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var resultEntity = await _dbContext.ResultEvents.SingleOrDefaultAsync(x => x.Id == resultEvent.Id, cancellationToken);
            if (resultEntity?.AppliedAt.HasValue == true)
            {
                return false;
            }

            if (resultEntity is null)
            {
                resultEntity = new ResultEventEntity
                {
                    Id = resultEvent.Id,
                    OrderId = mutation.OrderId,
                    Name = resultEvent.Name,
                    CorrelationId = resultEvent.CorrelationId,
                    IdempotencyKey = resultEvent.IdempotencyKey,
                    PayloadJson = JsonSerializer.Serialize(resultEvent, SerializerOptions),
                    OccurredAt = resultEvent.OccurredAt,
                };

                _dbContext.ResultEvents.Add(resultEntity);
            }

            resultEntity.ApplyAttemptCount++;
            resultEntity.LastApplyAttemptAt = DateTimeOffset.UtcNow;

            var orderEntity = await _dbContext.Orders.SingleOrDefaultAsync(x => x.Id == mutation.OrderId, cancellationToken)
                ?? throw new KeyNotFoundException($"Order '{mutation.OrderId}' was not found.");
            var tradeIntentEntity = await _dbContext.TradeIntents.SingleAsync(x => x.Id == orderEntity.TradeIntentId, cancellationToken);
            var order = MapOrder(orderEntity, tradeIntentEntity);
            var previousStatus = order.CurrentStatus;

            order.ApplyResult(
                mutation.ResultStatus,
                mutation.NextStatus,
                mutation.FilledQuantity,
                mutation.Details,
                mutation.ExternalOrderId,
                mutation.ClientOrderId,
                mutation.CommandEventId,
                resultEvent.OccurredAt);

            orderEntity.Status = order.CurrentStatus.ToString();
            orderEntity.PublishStatus = order.PublishStatus.ToString();
            orderEntity.LastResultStatus = order.LastResultStatus;
            orderEntity.LastResultMessage = order.LastResultMessage;
            orderEntity.ExternalOrderId = order.ExternalOrderId;
            orderEntity.ClientOrderId = order.ClientOrderId;
            orderEntity.CommandEventId = order.CommandEventId;
            orderEntity.FilledQuantity = order.FilledQuantity;
            orderEntity.UpdatedAt = order.UpdatedAt;

            _dbContext.OrderLifecycleEvents.Add(new OrderLifecycleEventEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Stage = resultEvent.Name,
                Details = mutation.Details,
                OccurredAt = resultEvent.OccurredAt,
            });

            if (mutation.NextStatus.HasValue && mutation.NextStatus.Value != previousStatus)
            {
                _dbContext.OrderEvents.Add(new OrderEventEntity
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Status = mutation.NextStatus.Value.ToString(),
                    FilledQuantity = order.FilledQuantity,
                    OccurredAt = resultEvent.OccurredAt,
                });
            }

            if (mutation.UpdatePositionSnapshot && order.TradeIntent.Side.HasValue && order.TradeIntent.LimitPrice.HasValue)
            {
                await UpsertPositionSnapshotEntityAsync(
                    new PositionSnapshot(
                        order.TradeIntent.Ticker,
                        order.TradeIntent.Side.Value,
                        order.FilledQuantity,
                        order.TradeIntent.LimitPrice.Value,
                        resultEvent.OccurredAt),
                    cancellationToken);
            }

            resultEntity.AppliedAt = DateTimeOffset.UtcNow;
            resultEntity.LastError = null;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        });

    public Task UpsertPositionSnapshotAsync(PositionSnapshot positionSnapshot, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("position-snapshots.upsert", async () =>
        {
            var existing = await _dbContext.PositionSnapshots.SingleOrDefaultAsync(x => x.Ticker == positionSnapshot.Ticker && x.Side == positionSnapshot.Side.ToString(), cancellationToken);
            if (existing is null)
            {
                _dbContext.PositionSnapshots.Add(new PositionSnapshotEntity
                {
                    Id = Guid.NewGuid(),
                    Ticker = positionSnapshot.Ticker,
                    Side = positionSnapshot.Side.ToString(),
                    Contracts = positionSnapshot.Contracts,
                    AveragePrice = positionSnapshot.AveragePrice,
                    AsOf = positionSnapshot.AsOf,
                });
            }
            else
            {
                existing.Contracts = positionSnapshot.Contracts;
                existing.AveragePrice = positionSnapshot.AveragePrice;
                existing.AsOf = positionSnapshot.AsOf;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync<IReadOnlyList<PositionSnapshot>>("position-snapshots.list", async () =>
        {
            var entities = await _dbContext.PositionSnapshots.AsNoTracking().OrderBy(x => x.Ticker).ToListAsync(cancellationToken);
            return entities.Select(MapPositionSnapshot).ToArray();
        });

    public Task<OutboxDispatchItem?> GetAsync(Guid messageId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("publisher-outbox.get", async () =>
        {
            var entity = await _dbContext.PublisherOutboxMessages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == messageId, cancellationToken);
            return entity is null ? null : MapOutboxDispatchItem(entity);
        });

    public Task<IReadOnlyList<OutboxDispatchItem>> AcquireDueMessagesAsync(
        int maxCount,
        string processorId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync<IReadOnlyList<OutboxDispatchItem>>("publisher-outbox.acquire-due", async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var entities = (await _dbContext.PublisherOutboxMessages
                .Where(x => x.Status == OutboxMessageStatus.Pending.ToString())
                .ToListAsync(cancellationToken))
                .Where(x => x.NextAttemptAt <= now)
                .OrderBy(x => x.NextAttemptAt)
                .ThenBy(x => x.CreatedAt)
                .Take(Math.Max(1, maxCount))
                .ToList();

            foreach (var entity in entities)
            {
                entity.Status = OutboxMessageStatus.InFlight.ToString();
                entity.ProcessorId = processorId;
                entity.LeaseExpiresAt = now.Add(leaseDuration);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return entities.Select(MapOutboxDispatchItem).ToArray();
        });

    public Task RecordAttemptAsync(
        Guid messageId,
        int attemptNumber,
        string outcome,
        string? failureKind,
        string? errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("publisher-outbox.record-attempt", async () =>
        {
            var message = await _dbContext.PublisherOutboxMessages.SingleAsync(x => x.Id == messageId, cancellationToken);
            message.AttemptCount = Math.Max(message.AttemptCount, attemptNumber);
            message.LastAttemptAt = occurredAt;
            message.LastFailureKind = failureKind;
            message.LastError = errorMessage;

            _dbContext.PublisherOutboxAttempts.Add(new PublisherOutboxAttemptEntity
            {
                Id = Guid.NewGuid(),
                MessageId = messageId,
                AttemptNumber = attemptNumber,
                Outcome = outcome,
                FailureKind = failureKind,
                ErrorMessage = errorMessage,
                OccurredAt = occurredAt,
            });

            var orderEntity = await _dbContext.Orders.SingleAsync(x => x.Id == message.AggregateId, cancellationToken);
            var tradeIntentEntity = await _dbContext.TradeIntents.SingleAsync(x => x.Id == orderEntity.TradeIntentId, cancellationToken);
            var order = MapOrder(orderEntity, tradeIntentEntity);
            order.MarkPublishAttempted(occurredAt);
            ApplyOrderState(orderEntity, order);

            _dbContext.OrderLifecycleEvents.Add(new OrderLifecycleEventEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Stage = "publish_attempted",
                Details = $"attempt={attemptNumber}",
                OccurredAt = occurredAt,
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task MarkPublishedAsync(Guid messageId, DateTimeOffset publishedAt, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("publisher-outbox.mark-published", async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var message = await _dbContext.PublisherOutboxMessages.SingleAsync(x => x.Id == messageId, cancellationToken);
            message.Status = OutboxMessageStatus.Published.ToString();
            message.PublishedAt = publishedAt;
            message.ProcessorId = null;
            message.LeaseExpiresAt = null;

            var orderEntity = await _dbContext.Orders.SingleAsync(x => x.Id == message.AggregateId, cancellationToken);
            var tradeIntentEntity = await _dbContext.TradeIntents.SingleAsync(x => x.Id == orderEntity.TradeIntentId, cancellationToken);
            var order = MapOrder(orderEntity, tradeIntentEntity);
            order.MarkPublishConfirmed(messageId, publishedAt);
            ApplyOrderState(orderEntity, order);

            _dbContext.OrderLifecycleEvents.Add(new OrderLifecycleEventEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Stage = "publish_confirmed",
                Details = $"commandEventId={messageId}",
                OccurredAt = publishedAt,
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

    public Task ScheduleRetryAsync(
        Guid messageId,
        DateTimeOffset nextAttemptAt,
        string failureKind,
        string errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("publisher-outbox.schedule-retry", async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var message = await _dbContext.PublisherOutboxMessages.SingleAsync(x => x.Id == messageId, cancellationToken);
            message.Status = OutboxMessageStatus.Pending.ToString();
            message.NextAttemptAt = nextAttemptAt;
            message.ProcessorId = null;
            message.LeaseExpiresAt = null;
            message.LastFailureKind = failureKind;
            message.LastError = errorMessage;

            var orderEntity = await _dbContext.Orders.SingleAsync(x => x.Id == message.AggregateId, cancellationToken);
            var tradeIntentEntity = await _dbContext.TradeIntents.SingleAsync(x => x.Id == orderEntity.TradeIntentId, cancellationToken);
            var order = MapOrder(orderEntity, tradeIntentEntity);
            order.MarkRetryScheduled(errorMessage, messageId, occurredAt);
            ApplyOrderState(orderEntity, order);

            _dbContext.OrderLifecycleEvents.Add(new OrderLifecycleEventEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Stage = "publish_retry_scheduled",
                Details = $"{failureKind}: {errorMessage}",
                OccurredAt = occurredAt,
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

    public Task MarkManualInterventionRequiredAsync(
        Guid messageId,
        string failureKind,
        string errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("publisher-outbox.manual-intervention", async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var message = await _dbContext.PublisherOutboxMessages.SingleAsync(x => x.Id == messageId, cancellationToken);
            message.Status = OutboxMessageStatus.ManualInterventionRequired.ToString();
            message.ProcessorId = null;
            message.LeaseExpiresAt = null;
            message.LastFailureKind = failureKind;
            message.LastError = errorMessage;

            var orderEntity = await _dbContext.Orders.SingleAsync(x => x.Id == message.AggregateId, cancellationToken);
            var tradeIntentEntity = await _dbContext.TradeIntents.SingleAsync(x => x.Id == orderEntity.TradeIntentId, cancellationToken);
            var order = MapOrder(orderEntity, tradeIntentEntity);
            order.MarkManualInterventionRequired(errorMessage, messageId, occurredAt);
            ApplyOrderState(orderEntity, order);

            _dbContext.OrderLifecycleEvents.Add(new OrderLifecycleEventEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Stage = "manual_intervention_required",
                Details = $"{failureKind}: {errorMessage}",
                OccurredAt = occurredAt,
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });

    public Task<int> ReleaseExpiredLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("publisher-outbox.release-expired", async () =>
        {
            var expired = (await _dbContext.PublisherOutboxMessages
                .Where(x => x.Status == OutboxMessageStatus.InFlight.ToString())
                .ToListAsync(cancellationToken))
                .Where(x => x.LeaseExpiresAt <= now)
                .ToList();

            foreach (var entity in expired)
            {
                entity.Status = OutboxMessageStatus.Pending.ToString();
                entity.ProcessorId = null;
                entity.LeaseExpiresAt = null;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return expired.Count;
        });

    public Task<OutboxHealthSnapshot> GetHealthSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("publisher-outbox.health", async () =>
        {
            var pendingQuery = _dbContext.PublisherOutboxMessages
                .AsNoTracking()
                .Where(x => x.Status == OutboxMessageStatus.Pending.ToString() || x.Status == OutboxMessageStatus.InFlight.ToString());

            var manualInterventionCount = await _dbContext.PublisherOutboxMessages
                .AsNoTracking()
                .LongCountAsync(x => x.Status == OutboxMessageStatus.ManualInterventionRequired.ToString(), cancellationToken);

            var pendingCount = await pendingQuery.LongCountAsync(cancellationToken);
            var pendingCreatedAt = await pendingQuery
                .Select(x => x.CreatedAt)
                .ToListAsync(cancellationToken);
            DateTimeOffset? oldestPendingCreatedAt = pendingCreatedAt.Count == 0
                ? null
                : pendingCreatedAt.Min();

            return new OutboxHealthSnapshot(pendingCount, manualInterventionCount, oldestPendingCreatedAt);
        });

    private async Task ExecuteDependencyCallAsync(string operation, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var dependencyName = GetDependencyName();

        try
        {
            await action();
            stopwatch.Stop();

            DependencyCallSucceeded(
                _logger,
                dependencyName,
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                null);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            DependencyCallFailed(
                _logger,
                dependencyName,
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                exception);

            throw;
        }
    }

    private async Task<T> ExecuteDependencyCallAsync<T>(string operation, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var dependencyName = GetDependencyName();

        try
        {
            var result = await action();
            stopwatch.Stop();

            DependencyCallSucceeded(
                _logger,
                dependencyName,
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                null);

            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            DependencyCallFailed(
                _logger,
                dependencyName,
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                exception);

            throw;
        }
    }

    private static TradeIntent MapTradeIntent(TradeIntentEntity entity)
    {
        var actionType = Enum.TryParse<TradeIntentActionType>(entity.ActionType, ignoreCase: true, out var parsedActionType)
            ? parsedActionType
            : TradeIntentActionType.Entry;

        return new TradeIntent(
                entity.Ticker,
                string.IsNullOrWhiteSpace(entity.Side) ? null : Enum.Parse<TradeSide>(entity.Side),
                entity.Quantity,
                entity.LimitPrice,
                entity.StrategyName,
                actionType,
                string.IsNullOrWhiteSpace(entity.OriginService) ? "legacy-client" : entity.OriginService,
                string.IsNullOrWhiteSpace(entity.DecisionReason) ? "legacy request" : entity.DecisionReason,
                string.IsNullOrWhiteSpace(entity.CommandSchemaVersion) ? "weather-quant-command.v1" : entity.CommandSchemaVersion,
                entity.TargetPositionTicker,
                string.IsNullOrWhiteSpace(entity.TargetPositionSide) ? null : Enum.Parse<TradeSide>(entity.TargetPositionSide),
                entity.TargetPublisherOrderId,
                entity.TargetClientOrderId,
                entity.TargetExternalOrderId,
                string.IsNullOrWhiteSpace(entity.CorrelationId) ? null : entity.CorrelationId,
                entity.CreatedAt)
            .WithId(entity.Id);
    }

    private static ExecutionEvent MapExecutionEvent(OrderEventEntity entity)
    {
        return new ExecutionEvent(entity.OrderId, Enum.Parse<OrderStatus>(entity.Status), entity.FilledQuantity, entity.OccurredAt)
            .WithId(entity.Id);
    }

    private static PositionSnapshot MapPositionSnapshot(PositionSnapshotEntity entity)
    {
        return new PositionSnapshot(entity.Ticker, Enum.Parse<TradeSide>(entity.Side), entity.Contracts, entity.AveragePrice, entity.AsOf);
    }

    private async Task UpsertPositionSnapshotEntityAsync(PositionSnapshot positionSnapshot, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.PositionSnapshots.SingleOrDefaultAsync(
            x => x.Ticker == positionSnapshot.Ticker && x.Side == positionSnapshot.Side.ToString(),
            cancellationToken);

        if (existing is null)
        {
            _dbContext.PositionSnapshots.Add(new PositionSnapshotEntity
            {
                Id = Guid.NewGuid(),
                Ticker = positionSnapshot.Ticker,
                Side = positionSnapshot.Side.ToString(),
                Contracts = positionSnapshot.Contracts,
                AveragePrice = positionSnapshot.AveragePrice,
                AsOf = positionSnapshot.AsOf,
            });
        }
        else
        {
            existing.Contracts = positionSnapshot.Contracts;
            existing.AveragePrice = positionSnapshot.AveragePrice;
            existing.AsOf = positionSnapshot.AsOf;
        }
    }

    private static void ApplyOrderState(OrderEntity entity, Order order)
    {
        entity.Status = order.CurrentStatus.ToString();
        entity.PublishStatus = order.PublishStatus.ToString();
        entity.LastResultStatus = order.LastResultStatus;
        entity.LastResultMessage = order.LastResultMessage;
        entity.ExternalOrderId = order.ExternalOrderId;
        entity.ClientOrderId = order.ClientOrderId;
        entity.CommandEventId = order.CommandEventId;
        entity.FilledQuantity = order.FilledQuantity;
        entity.UpdatedAt = order.UpdatedAt;
    }

    private static OutboxDispatchItem MapOutboxDispatchItem(PublisherOutboxMessageEntity entity)
    {
        var status = Enum.TryParse<OutboxMessageStatus>(entity.Status, ignoreCase: true, out var parsedStatus)
            ? parsedStatus
            : OutboxMessageStatus.Pending;

        return new OutboxDispatchItem(
            entity.Id,
            entity.AggregateId,
            entity.AggregateType,
            entity.PayloadJson,
            entity.AttemptCount,
            entity.CreatedAt,
            entity.LastAttemptAt,
            entity.NextAttemptAt,
            entity.LeaseExpiresAt,
            entity.LastError,
            status);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException?.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) == true
            || exception.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
    }

    private string GetDependencyName()
    {
        return DatabaseProviders.GetDependencyName(_dbContext.Database.ProviderName);
    }

    private static Order MapOrder(OrderEntity orderEntity, TradeIntentEntity tradeIntentEntity)
    {
        var tradeIntent = MapTradeIntent(tradeIntentEntity);
        var order = new Order(tradeIntent);
        var publishStatus = Enum.TryParse<OrderPublishStatus>(orderEntity.PublishStatus, ignoreCase: true, out var parsedPublishStatus)
            ? parsedPublishStatus
            : string.Equals(orderEntity.PublishStatus, "legacy", StringComparison.OrdinalIgnoreCase)
                ? OrderPublishStatus.PublishConfirmed
                : string.Equals(orderEntity.PublishStatus, "PublishPendingReview", StringComparison.OrdinalIgnoreCase)
                    ? OrderPublishStatus.ManualInterventionRequired
                : OrderPublishStatus.OrderCreated;
        order.SetPersistenceState(
            orderEntity.Id,
            Enum.Parse<OrderStatus>(orderEntity.Status),
            publishStatus,
            orderEntity.LastResultStatus,
            orderEntity.LastResultMessage,
            orderEntity.ExternalOrderId,
            orderEntity.ClientOrderId,
            orderEntity.CommandEventId,
            orderEntity.FilledQuantity,
            orderEntity.CreatedAt,
            orderEntity.UpdatedAt);
        return order;
    }
}
