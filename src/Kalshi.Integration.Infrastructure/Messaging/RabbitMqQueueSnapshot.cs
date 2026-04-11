namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Represents a sampled RabbitMQ queue state used by reliability health checks and metrics.
/// </summary>
public sealed record RabbitMqQueueSnapshot(
    string QueueName,
    bool IsCritical,
    bool IsDeadLetter,
    long MessageCount,
    long ConsumerCount,
    DateTimeOffset? NonEmptySince,
    long GrowthSincePreviousSample)
{
    public TimeSpan? GetBacklogAge(DateTimeOffset capturedAt)
        => MessageCount > 0 && NonEmptySince.HasValue
            ? capturedAt - NonEmptySince.Value
            : null;
}

/// <summary>
/// Represents a point-in-time snapshot of all monitored RabbitMQ queues.
/// </summary>
public sealed record RabbitMqQueueDiagnosticsSnapshot(
    DateTimeOffset CapturedAt,
    IReadOnlyList<RabbitMqQueueSnapshot> Queues);
