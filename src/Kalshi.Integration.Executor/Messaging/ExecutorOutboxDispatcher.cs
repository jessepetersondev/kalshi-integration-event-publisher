using System.Text.Json;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Contracts.Diagnostics;
using Kalshi.Integration.Contracts.Reliability;
using Kalshi.Integration.Executor.Persistence;
using Kalshi.Integration.Executor.Persistence.Entities;
using Kalshi.Integration.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Executor.Messaging;

public sealed class ExecutorOutboxDispatcher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ExecutorDbContext _dbContext;
    private readonly IApplicationEventPublisher _applicationEventPublisher;
    private readonly ExecutorOperationalIssueRecorder _issueRecorder;
    private readonly ILogger<ExecutorOutboxDispatcher> _logger;
    private readonly RabbitMqOptions _options;
    private readonly string _processorId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public ExecutorOutboxDispatcher(
        ExecutorDbContext dbContext,
        IApplicationEventPublisher applicationEventPublisher,
        ExecutorOperationalIssueRecorder issueRecorder,
        IOptions<RabbitMqOptions> options,
        ILogger<ExecutorOutboxDispatcher> logger)
    {
        _dbContext = dbContext;
        _applicationEventPublisher = applicationEventPublisher;
        _issueRecorder = issueRecorder;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> DrainDueMessagesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = (await _dbContext.OutboxMessages
            .Where(x => x.Status == OutboxMessageStatus.InFlight.ToString())
            .ToListAsync(cancellationToken))
            .Where(x => x.LeaseExpiresAt <= now)
            .ToList();

        foreach (var message in expired)
        {
            message.Status = OutboxMessageStatus.Pending.ToString();
            message.ProcessorId = null;
            message.LeaseExpiresAt = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var due = (await _dbContext.OutboxMessages
            .Where(x => x.Status == OutboxMessageStatus.Pending.ToString())
            .ToListAsync(cancellationToken))
            .Where(x => x.NextAttemptAt <= now)
            .OrderBy(x => x.NextAttemptAt)
            .ThenBy(x => x.CreatedAt)
            .Take(_options.OutboxBatchSize)
            .ToList();

        foreach (var message in due)
        {
            message.Status = OutboxMessageStatus.InFlight.ToString();
            message.ProcessorId = _processorId;
            message.LeaseExpiresAt = now.AddSeconds(_options.OutboxLeaseDurationSeconds);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var message in due)
        {
            await ProcessAsync(message, cancellationToken);
        }

        return due.Count;
    }

    private async Task ProcessAsync(ExecutorOutboxMessageEntity message, CancellationToken cancellationToken)
    {
        var attemptNumber = message.AttemptCount + 1;
        var attemptedAt = DateTimeOffset.UtcNow;

        try
        {
            var envelope = JsonSerializer.Deserialize<ApplicationEventEnvelope>(message.PayloadJson, SerializerOptions)
                ?? throw new InvalidOperationException($"Executor outbox message '{message.Id}' payload could not be deserialized.");

            await _applicationEventPublisher.PublishAsync(envelope, cancellationToken);

            message.AttemptCount = attemptNumber;
            message.LastAttemptAt = attemptedAt;
            message.Status = OutboxMessageStatus.Published.ToString();
            message.PublishedAt = DateTimeOffset.UtcNow;
            message.LeaseExpiresAt = null;
            message.ProcessorId = null;
            message.LastError = null;
            message.LastFailureKind = null;

            _dbContext.OutboxAttempts.Add(new ExecutorOutboxAttemptEntity
            {
                Id = Guid.NewGuid(),
                MessageId = message.Id,
                AttemptNumber = attemptNumber,
                Outcome = "succeeded",
                OccurredAt = attemptedAt,
            });

            var execution = await _dbContext.ExecutionRecords.SingleAsync(x => x.Id == message.ExecutionRecordId, cancellationToken);
            if (message.MessageType == "result")
            {
                execution.TerminalResultPublishedAt ??= message.PublishedAt;
                execution.UpdatedAt = attemptedAt;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            message.AttemptCount = attemptNumber;
            message.LastAttemptAt = attemptedAt;
            message.LastError = exception.Message;
            var failureKind = ClassifyFailure(exception);
            message.LastFailureKind = failureKind;

            _dbContext.OutboxAttempts.Add(new ExecutorOutboxAttemptEntity
            {
                Id = Guid.NewGuid(),
                MessageId = message.Id,
                AttemptNumber = attemptNumber,
                Outcome = "failed",
                FailureKind = failureKind,
                ErrorMessage = exception.Message,
                OccurredAt = attemptedAt,
            });

            if (attemptNumber >= _options.OutboxMaxAttempts)
            {
                message.Status = OutboxMessageStatus.ManualInterventionRequired.ToString();
                message.LeaseExpiresAt = null;
                message.ProcessorId = null;

                KalshiTelemetry.ReliabilityRetryExhaustedTotal.Add(
                    1,
                    new KeyValuePair<string, object?>("component", "executor"),
                    new KeyValuePair<string, object?>("outbox", message.MessageType),
                    new KeyValuePair<string, object?>("failure_kind", failureKind));
            }
            else
            {
                message.Status = OutboxMessageStatus.Pending.ToString();
                message.NextAttemptAt = attemptedAt.Add(CalculateRetryDelay(attemptNumber));
                message.LeaseExpiresAt = null;
                message.ProcessorId = null;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (message.Status == OutboxMessageStatus.ManualInterventionRequired.ToString())
            {
                await _issueRecorder.AddAsync(
                    "reliability",
                    "error",
                    "executor-outbox",
                    $"Executor outbox exhausted retries for message '{message.Id}'.",
                    $"{failureKind}: {exception.Message}",
                    cancellationToken);
            }

            _logger.LogWarning(exception, "Executor outbox message {MessageId} failed on attempt {AttemptNumber}.", message.Id, attemptNumber);
        }
    }

    private TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        var exponent = Math.Max(0, attemptNumber - 1);
        var baseDelay = Math.Min(
            _options.OutboxMaxRetryDelayMilliseconds,
            _options.OutboxInitialRetryDelayMilliseconds * Math.Pow(2, exponent));
        var jitter = _options.OutboxJitterMaxMilliseconds <= 0
            ? 0
            : Random.Shared.Next(0, _options.OutboxJitterMaxMilliseconds + 1);
        return TimeSpan.FromMilliseconds(baseDelay + jitter);
    }

    private static string ClassifyFailure(Exception exception)
        => exception switch
        {
            PublishConfirmationException publishFailure => publishFailure.FailureKind.ToString().ToLowerInvariant(),
            TimeoutException => "confirm_timeout",
            _ => exception.GetType().Name,
        };
}
