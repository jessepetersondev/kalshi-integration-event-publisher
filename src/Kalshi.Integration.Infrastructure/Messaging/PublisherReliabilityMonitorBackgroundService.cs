using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Contracts.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Samples publisher-side reliability state, emits telemetry, and records operator-visible issues on degradation.
/// </summary>
public sealed class PublisherReliabilityMonitorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly RabbitMqQueueInspector _queueInspector;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<PublisherReliabilityMonitorBackgroundService> _logger;
    private readonly HashSet<string> _queuesWithoutConsumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _deadLetterCounts = new(StringComparer.Ordinal);
    private bool _queueInspectionFailed;
    private long _manualInterventionCount;
    private int _outboxDelaySeverity;

    public PublisherReliabilityMonitorBackgroundService(
        IServiceScopeFactory serviceScopeFactory,
        RabbitMqQueueInspector queueInspector,
        IOptions<RabbitMqOptions> options,
        ILogger<PublisherReliabilityMonitorBackgroundService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _queueInspector = queueInspector;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var outboxStore = scope.ServiceProvider.GetRequiredService<IPublisherCommandOutboxStore>();
                var issueStore = scope.ServiceProvider.GetRequiredService<IOperationalIssueStore>();
                var now = DateTimeOffset.UtcNow;
                var outboxSnapshot = await outboxStore.GetHealthSnapshotAsync(now, stoppingToken);

                RecordOutboxMetrics(now, outboxSnapshot);
                await RaiseOutboxIssuesAsync(issueStore, now, outboxSnapshot, stoppingToken);

                var queueSnapshot = await _queueInspector.CaptureAsync(stoppingToken);
                _queueInspectionFailed = false;
                RecordQueueMetrics(queueSnapshot);
                await RaiseQueueIssuesAsync(issueStore, queueSnapshot, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                KalshiTelemetry.RabbitMqReconnectFailuresTotal.Add(
                    1,
                    new KeyValuePair<string, object?>("component", "publisher"));

                if (!_queueInspectionFailed)
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    var issueStore = scope.ServiceProvider.GetRequiredService<IOperationalIssueStore>();
                    await issueStore.AddAsync(
                        OperationalIssue.Create(
                            category: "reliability",
                            severity: "error",
                            source: "publisher-monitor",
                            message: "Publisher reliability monitoring failed to inspect RabbitMQ or outbox state.",
                            details: exception.Message),
                        stoppingToken);
                }

                _queueInspectionFailed = true;
                _logger.LogError(exception, "Publisher reliability monitor iteration failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.QueueMonitoringIntervalMilliseconds), stoppingToken);
        }
    }

    private static void RecordOutboxMetrics(DateTimeOffset now, Contracts.Reliability.OutboxHealthSnapshot outboxSnapshot)
    {
        KalshiTelemetry.OutboxPendingCount.Record(
            outboxSnapshot.PendingCount,
            new KeyValuePair<string, object?>("component", "publisher"),
            new KeyValuePair<string, object?>("outbox", "command"));

        var oldestPendingAgeMs = outboxSnapshot.OldestPendingCreatedAt.HasValue
            ? Math.Max(0, (now - outboxSnapshot.OldestPendingCreatedAt.Value).TotalMilliseconds)
            : 0;

        KalshiTelemetry.OutboxOldestPendingAgeMs.Record(
            oldestPendingAgeMs,
            new KeyValuePair<string, object?>("component", "publisher"),
            new KeyValuePair<string, object?>("outbox", "command"));
    }

    private async Task RaiseOutboxIssuesAsync(
        IOperationalIssueStore issueStore,
        DateTimeOffset now,
        Contracts.Reliability.OutboxHealthSnapshot outboxSnapshot,
        CancellationToken cancellationToken)
    {
        if (outboxSnapshot.ManualInterventionCount > _manualInterventionCount)
        {
            await issueStore.AddAsync(
                OperationalIssue.Create(
                    category: "reliability",
                    severity: "error",
                    source: "publisher-outbox",
                    message: "Publisher command outbox messages require manual intervention.",
                    details: $"manualInterventionCount={outboxSnapshot.ManualInterventionCount}"),
                cancellationToken);
        }

        _manualInterventionCount = outboxSnapshot.ManualInterventionCount;

        if (!outboxSnapshot.OldestPendingCreatedAt.HasValue)
        {
            _outboxDelaySeverity = 0;
            return;
        }

        var oldestAgeSeconds = (now - outboxSnapshot.OldestPendingCreatedAt.Value).TotalSeconds;
        var severity = oldestAgeSeconds >= _options.OutboxUnhealthyAgeSeconds
            ? 2
            : oldestAgeSeconds >= _options.OutboxDegradedAgeSeconds
                ? 1
                : 0;

        if (severity == 0 || severity == _outboxDelaySeverity)
        {
            _outboxDelaySeverity = severity;
            return;
        }

        _outboxDelaySeverity = severity;
        await issueStore.AddAsync(
            OperationalIssue.Create(
                category: "reliability",
                severity: severity == 2 ? "error" : "warning",
                source: "publisher-outbox",
                message: "Publisher command outbox is delayed.",
                details: $"pendingCount={outboxSnapshot.PendingCount}; oldestPendingAgeSeconds={oldestAgeSeconds:F0}"),
            cancellationToken);
    }

    private static void RecordQueueMetrics(RabbitMqQueueDiagnosticsSnapshot snapshot)
    {
        foreach (var queue in snapshot.Queues)
        {
            var tags = new[]
            {
                new KeyValuePair<string, object?>("component", "publisher"),
                new KeyValuePair<string, object?>("queue", queue.QueueName),
                new KeyValuePair<string, object?>("queue_role", queue.IsDeadLetter ? "dead_letter" : "critical"),
            };

            KalshiTelemetry.RabbitMqQueueBacklogCount.Record(queue.MessageCount, tags);
            KalshiTelemetry.RabbitMqQueueConsumerCount.Record(queue.ConsumerCount, tags);
            KalshiTelemetry.RabbitMqQueueBacklogAgeMs.Record(queue.GetBacklogAge(snapshot.CapturedAt)?.TotalMilliseconds ?? 0, tags);

            if (queue.IsDeadLetter)
            {
                KalshiTelemetry.RabbitMqDeadLetterQueueSize.Record(queue.MessageCount, tags);
                if (queue.GrowthSincePreviousSample > 0)
                {
                    KalshiTelemetry.RabbitMqDeadLetterQueueGrowthTotal.Add(queue.GrowthSincePreviousSample, tags);
                }
            }
        }
    }

    private async Task RaiseQueueIssuesAsync(
        IOperationalIssueStore issueStore,
        RabbitMqQueueDiagnosticsSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        foreach (var queue in snapshot.Queues)
        {
            if (queue.IsCritical)
            {
                if (queue.ConsumerCount == 0)
                {
                    if (_queuesWithoutConsumers.Add(queue.QueueName))
                    {
                        await issueStore.AddAsync(
                            OperationalIssue.Create(
                                category: "reliability",
                                severity: "error",
                                source: "rabbitmq-queues",
                                message: $"Critical RabbitMQ queue '{queue.QueueName}' has no consumers.",
                                details: $"messageCount={queue.MessageCount}"),
                            cancellationToken);
                    }
                }
                else
                {
                    _queuesWithoutConsumers.Remove(queue.QueueName);
                }
            }

            if (queue.IsDeadLetter)
            {
                _deadLetterCounts.TryGetValue(queue.QueueName, out var previousCount);
                _deadLetterCounts[queue.QueueName] = queue.MessageCount;

                if (queue.MessageCount > 0 && queue.MessageCount > previousCount)
                {
                    await issueStore.AddAsync(
                        OperationalIssue.Create(
                            category: "reliability",
                            severity: "warning",
                            source: "rabbitmq-dlq",
                            message: $"Dead-letter queue '{queue.QueueName}' grew.",
                            details: $"previousCount={previousCount}; currentCount={queue.MessageCount}"),
                        cancellationToken);
                }
            }
        }
    }
}
