using Kalshi.Integration.Contracts.Diagnostics;
using Kalshi.Integration.Executor.Persistence;
using Kalshi.Integration.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Executor.Messaging;

/// <summary>
/// Samples executor reliability state, emits telemetry, and persists operational issues on degradation.
/// </summary>
public sealed class ExecutorReliabilityMonitorBackgroundService(
    IServiceScopeFactory serviceScopeFactory,
    RabbitMqQueueInspector queueInspector,
    IOptions<RabbitMqOptions> options,
    ILogger<ExecutorReliabilityMonitorBackgroundService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly RabbitMqQueueInspector _queueInspector = queueInspector;
    private readonly RabbitMqOptions _options = options.Value;
    private readonly ILogger<ExecutorReliabilityMonitorBackgroundService> _logger = logger;
    private readonly HashSet<string> _queuesWithoutConsumers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _deadLetterCounts = new(StringComparer.Ordinal);
    private bool _queueInspectionFailed;
    private long _manualInterventionCount;
    private int _outboxDelaySeverity;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using IServiceScope scope = _serviceScopeFactory.CreateScope();
                ExecutorOutboxHealthService outboxHealthService = scope.ServiceProvider.GetRequiredService<ExecutorOutboxHealthService>();
                ExecutorOperationalIssueRecorder issueRecorder = scope.ServiceProvider.GetRequiredService<ExecutorOperationalIssueRecorder>();
                DateTimeOffset now = DateTimeOffset.UtcNow;
                Contracts.Reliability.OutboxHealthSnapshot outboxSnapshot = await outboxHealthService.GetSnapshotAsync(now, stoppingToken);

                RecordOutboxMetrics(now, outboxSnapshot);
                await RaiseOutboxIssuesAsync(issueRecorder, now, outboxSnapshot, stoppingToken);

                RabbitMqQueueDiagnosticsSnapshot queueSnapshot = await _queueInspector.CaptureAsync(stoppingToken);
                _queueInspectionFailed = false;
                RecordQueueMetrics(queueSnapshot);
                await RaiseQueueIssuesAsync(issueRecorder, queueSnapshot, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                KalshiTelemetry.RabbitMqReconnectFailuresTotal.Add(
                    1,
                    new KeyValuePair<string, object?>("component", "executor"));

                if (!_queueInspectionFailed)
                {
                    using IServiceScope scope = _serviceScopeFactory.CreateScope();
                    ExecutorOperationalIssueRecorder issueRecorder = scope.ServiceProvider.GetRequiredService<ExecutorOperationalIssueRecorder>();
                    await issueRecorder.AddAsync(
                        "reliability",
                        "error",
                        "executor-monitor",
                        "Executor reliability monitoring failed to inspect RabbitMQ or outbox state.",
                        exception.Message,
                        stoppingToken);
                }

                _queueInspectionFailed = true;
                _logger.LogError(exception, "Executor reliability monitor iteration failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(_options.QueueMonitoringIntervalMilliseconds), stoppingToken);
        }
    }

    private static void RecordOutboxMetrics(DateTimeOffset now, Contracts.Reliability.OutboxHealthSnapshot outboxSnapshot)
    {
        KalshiTelemetry.OutboxPendingCount.Record(
            outboxSnapshot.PendingCount,
            new KeyValuePair<string, object?>("component", "executor"),
            new KeyValuePair<string, object?>("outbox", "events"));

        double oldestPendingAgeMs = outboxSnapshot.OldestPendingCreatedAt.HasValue
            ? Math.Max(0, (now - outboxSnapshot.OldestPendingCreatedAt.Value).TotalMilliseconds)
            : 0;

        KalshiTelemetry.OutboxOldestPendingAgeMs.Record(
            oldestPendingAgeMs,
            new KeyValuePair<string, object?>("component", "executor"),
            new KeyValuePair<string, object?>("outbox", "events"));
    }

    private async Task RaiseOutboxIssuesAsync(
        ExecutorOperationalIssueRecorder issueRecorder,
        DateTimeOffset now,
        Contracts.Reliability.OutboxHealthSnapshot outboxSnapshot,
        CancellationToken cancellationToken)
    {
        if (outboxSnapshot.ManualInterventionCount > _manualInterventionCount)
        {
            await issueRecorder.AddAsync(
                "reliability",
                "error",
                "executor-outbox",
                "Executor outbox messages require manual intervention.",
                $"manualInterventionCount={outboxSnapshot.ManualInterventionCount}",
                cancellationToken);
        }

        _manualInterventionCount = outboxSnapshot.ManualInterventionCount;

        if (!outboxSnapshot.OldestPendingCreatedAt.HasValue)
        {
            _outboxDelaySeverity = 0;
            return;
        }

        double oldestAgeSeconds = (now - outboxSnapshot.OldestPendingCreatedAt.Value).TotalSeconds;
        int severity = oldestAgeSeconds >= _options.OutboxUnhealthyAgeSeconds
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
        await issueRecorder.AddAsync(
            "reliability",
            severity == 2 ? "error" : "warning",
            "executor-outbox",
            "Executor outbox is delayed.",
            $"pendingCount={outboxSnapshot.PendingCount}; oldestPendingAgeSeconds={oldestAgeSeconds:F0}",
            cancellationToken);
    }

    private static void RecordQueueMetrics(RabbitMqQueueDiagnosticsSnapshot snapshot)
    {
        foreach (RabbitMqQueueSnapshot queue in snapshot.Queues)
        {
            KeyValuePair<string, object?>[] tags =
            [
                new KeyValuePair<string, object?>("component", "executor"),
                new KeyValuePair<string, object?>("queue", queue.QueueName),
                new KeyValuePair<string, object?>("queue_role", queue.IsDeadLetter ? "dead_letter" : "critical"),
            ];

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
        ExecutorOperationalIssueRecorder issueRecorder,
        RabbitMqQueueDiagnosticsSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        foreach (RabbitMqQueueSnapshot queue in snapshot.Queues)
        {
            if (queue.IsCritical)
            {
                if (queue.ConsumerCount == 0)
                {
                    if (_queuesWithoutConsumers.Add(queue.QueueName))
                    {
                        await issueRecorder.AddAsync(
                            "reliability",
                            "error",
                            "rabbitmq-queues",
                            $"Critical RabbitMQ queue '{queue.QueueName}' has no consumers.",
                            $"messageCount={queue.MessageCount}",
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
                _deadLetterCounts.TryGetValue(queue.QueueName, out long previousCount);
                _deadLetterCounts[queue.QueueName] = queue.MessageCount;

                if (queue.MessageCount > 0 && queue.MessageCount > previousCount)
                {
                    await issueRecorder.AddAsync(
                        "reliability",
                        "warning",
                        "rabbitmq-dlq",
                        $"Dead-letter queue '{queue.QueueName}' grew.",
                        $"previousCount={previousCount}; currentCount={queue.MessageCount}",
                        cancellationToken);
                }
            }
        }
    }
}
