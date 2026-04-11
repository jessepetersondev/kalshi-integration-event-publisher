using Kalshi.Integration.Contracts.Reliability;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Owns durable publisher-command outbox persistence and state transitions.
/// </summary>
public interface IPublisherCommandOutboxStore
{
    Task<OutboxDispatchItem?> GetAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutboxDispatchItem>> AcquireDueMessagesAsync(
        int maxCount,
        string processorId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default);

    Task RecordAttemptAsync(
        Guid messageId,
        int attemptNumber,
        string outcome,
        string? failureKind,
        string? errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    Task MarkPublishedAsync(Guid messageId, DateTimeOffset publishedAt, CancellationToken cancellationToken = default);

    Task ScheduleRetryAsync(
        Guid messageId,
        DateTimeOffset nextAttemptAt,
        string failureKind,
        string errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    Task MarkManualInterventionRequiredAsync(
        Guid messageId,
        string failureKind,
        string errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    Task<int> ReleaseExpiredLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    Task<OutboxHealthSnapshot> GetHealthSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
