using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Reports RabbitMQ queue health based on consumer presence, backlog age, and DLQ growth/size.
/// </summary>
public sealed class RabbitMqQueuesHealthCheck(
    RabbitMqQueueInspector queueInspector,
    IOptions<RabbitMqOptions> options) : IHealthCheck
{
    private readonly RabbitMqQueueInspector _queueInspector = queueInspector;
    private readonly RabbitMqOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        RabbitMqQueueDiagnosticsSnapshot snapshot;

        try
        {
            snapshot = await _queueInspector.CaptureAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ queue inspection failed.", exception);
        }

        DateTimeOffset now = snapshot.CapturedAt;
        Dictionary<string, object> data = [];

        foreach (RabbitMqQueueSnapshot queue in snapshot.Queues)
        {
            string key = NormalizeQueueName(queue.QueueName);
            data[$"{key}.messageCount"] = queue.MessageCount;
            data[$"{key}.consumerCount"] = queue.ConsumerCount;
            data[$"{key}.growthSincePreviousSample"] = queue.GrowthSincePreviousSample;

            TimeSpan? backlogAge = queue.GetBacklogAge(now);
            if (backlogAge.HasValue)
            {
                data[$"{key}.backlogAgeSeconds"] = backlogAge.Value.TotalSeconds;
            }
        }

        RabbitMqQueueSnapshot? zeroConsumerCriticalQueue = snapshot.Queues
            .FirstOrDefault(queue => queue.IsCritical && queue.ConsumerCount == 0);
        if (zeroConsumerCriticalQueue is not null)
        {
            return HealthCheckResult.Unhealthy(
                $"Critical RabbitMQ queue '{zeroConsumerCriticalQueue.QueueName}' has no active consumers.",
                data: data);
        }

        RabbitMqQueueSnapshot? unhealthyBacklogQueue = snapshot.Queues
            .FirstOrDefault(queue => queue.IsCritical
                && queue.GetBacklogAge(now)?.TotalSeconds >= _options.CriticalQueueBacklogUnhealthyAgeSeconds);
        if (unhealthyBacklogQueue is not null)
        {
            return HealthCheckResult.Unhealthy(
                $"Critical RabbitMQ queue '{unhealthyBacklogQueue.QueueName}' backlog age exceeded unhealthy threshold.",
                data: data);
        }

        RabbitMqQueueSnapshot? degradedBacklogQueue = snapshot.Queues
            .FirstOrDefault(queue => queue.IsCritical
                && queue.GetBacklogAge(now)?.TotalSeconds >= _options.CriticalQueueBacklogDegradedAgeSeconds);
        if (degradedBacklogQueue is not null)
        {
            return HealthCheckResult.Degraded(
                $"Critical RabbitMQ queue '{degradedBacklogQueue.QueueName}' is backlogged.",
                data: data);
        }

        RabbitMqQueueSnapshot? deadLetterQueue = snapshot.Queues.FirstOrDefault(queue => queue.IsDeadLetter && queue.MessageCount > 0);
        if (deadLetterQueue is not null)
        {
            return HealthCheckResult.Degraded(
                $"RabbitMQ dead-letter queue '{deadLetterQueue.QueueName}' contains messages.",
                data: data);
        }

        return HealthCheckResult.Healthy("RabbitMQ queues are healthy.", data);
    }

    private static string NormalizeQueueName(string queueName)
        => queueName.Replace('.', '_').Replace('-', '_');
}
