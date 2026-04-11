using System.Text.Json;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Contracts.Diagnostics;
using Kalshi.Integration.Contracts.Reliability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Drains the publisher command outbox and advances durable message state based on
/// broker publication outcomes.
/// </summary>
public sealed class PublisherCommandOutboxDispatcher(
    IPublisherCommandOutboxStore outboxStore,
    IApplicationEventPublisher applicationEventPublisher,
    IOperationalIssueStore issueStore,
    IOptions<RabbitMqOptions> options,
    ILogger<PublisherCommandOutboxDispatcher> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IPublisherCommandOutboxStore _outboxStore = outboxStore;
    private readonly IApplicationEventPublisher _applicationEventPublisher = applicationEventPublisher;
    private readonly IOperationalIssueStore _issueStore = issueStore;
    private readonly ILogger<PublisherCommandOutboxDispatcher> _logger = logger;
    private readonly RabbitMqOptions _options = options.Value;
    private readonly string _processorId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public async Task DispatchAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        OutboxDispatchItem? item = await _outboxStore.GetAsync(messageId, cancellationToken);
        if (item is null || item.Status is OutboxMessageStatus.Published or OutboxMessageStatus.ManualInterventionRequired)
        {
            return;
        }

        await ProcessAsync(item, cancellationToken);
    }

    public async Task<int> DrainDueMessagesAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await _outboxStore.ReleaseExpiredLeasesAsync(now, cancellationToken);
        IReadOnlyList<OutboxDispatchItem> acquired = await _outboxStore.AcquireDueMessagesAsync(
            _options.OutboxBatchSize,
            _processorId,
            now,
            TimeSpan.FromSeconds(_options.OutboxLeaseDurationSeconds),
            cancellationToken);

        foreach (OutboxDispatchItem item in acquired)
        {
            await ProcessAsync(item, cancellationToken);
        }

        return acquired.Count;
    }

    private async Task ProcessAsync(OutboxDispatchItem item, CancellationToken cancellationToken)
    {
        int attemptNumber = item.AttemptCount + 1;
        DateTimeOffset attemptedAt = DateTimeOffset.UtcNow;

        try
        {
            ApplicationEventEnvelope envelope = JsonSerializer.Deserialize<ApplicationEventEnvelope>(item.PayloadJson, SerializerOptions)
                ?? throw new InvalidOperationException($"Outbox message '{item.MessageId}' payload could not be deserialized.");

            await _applicationEventPublisher.PublishAsync(envelope, cancellationToken);
            await _outboxStore.RecordAttemptAsync(item.MessageId, attemptNumber, "succeeded", null, null, attemptedAt, cancellationToken);
            await _outboxStore.MarkPublishedAsync(item.MessageId, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (Exception exception)
        {
            string failureKind = ClassifyFailure(exception);
            string errorMessage = exception.Message;

            await _outboxStore.RecordAttemptAsync(item.MessageId, attemptNumber, "failed", failureKind, errorMessage, attemptedAt, cancellationToken);

            if (attemptNumber >= _options.OutboxMaxAttempts || IsPermanentFailure(exception))
            {
                KalshiTelemetry.ReliabilityRetryExhaustedTotal.Add(
                    1,
                    new KeyValuePair<string, object?>("component", "publisher"),
                    new KeyValuePair<string, object?>("outbox", "command"),
                    new KeyValuePair<string, object?>("failure_kind", failureKind));

                await _outboxStore.MarkManualInterventionRequiredAsync(item.MessageId, failureKind, errorMessage, attemptedAt, cancellationToken);
                await _issueStore.AddAsync(
                    OperationalIssue.Create(
                        category: "reliability",
                        severity: "error",
                        source: "publisher-outbox",
                        message: $"Publisher command outbox exhausted for message {item.MessageId}.",
                        details: $"{failureKind}: {errorMessage}"),
                    cancellationToken);

                _logger.LogError(exception, "Publisher outbox message {MessageId} requires manual intervention after attempt {AttemptNumber}.", item.MessageId, attemptNumber);
                return;
            }

            DateTimeOffset nextAttemptAt = attemptedAt.Add(CalculateRetryDelay(attemptNumber));
            await _outboxStore.ScheduleRetryAsync(item.MessageId, nextAttemptAt, failureKind, errorMessage, attemptedAt, cancellationToken);
            _logger.LogWarning(exception, "Publisher outbox message {MessageId} failed on attempt {AttemptNumber}. Retrying at {NextAttemptAt}.", item.MessageId, attemptNumber, nextAttemptAt);
        }
    }

    private TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        int exponent = Math.Max(0, attemptNumber - 1);
        double baseDelay = Math.Min(
            _options.OutboxMaxRetryDelayMilliseconds,
            _options.OutboxInitialRetryDelayMilliseconds * Math.Pow(2, exponent));
        int jitter = _options.OutboxJitterMaxMilliseconds <= 0
            ? 0
            : Random.Shared.Next(0, _options.OutboxJitterMaxMilliseconds + 1);
        return TimeSpan.FromMilliseconds(baseDelay + jitter);
    }

    private static string ClassifyFailure(Exception exception)
    {
        return exception switch
        {
            PublishConfirmationException publishFailure => publishFailure.FailureKind.ToString().ToLowerInvariant(),
            TimeoutException => "confirm_timeout",
            OperationCanceledException => "canceled",
            _ => "publish_failed",
        };
    }

    private static bool IsPermanentFailure(Exception exception)
    {
        string message = exception.Message;
        return message.Contains("NOT_FOUND", StringComparison.OrdinalIgnoreCase)
            || message.Contains("ACCESS_REFUSED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("configuration", StringComparison.OrdinalIgnoreCase);
    }
}
