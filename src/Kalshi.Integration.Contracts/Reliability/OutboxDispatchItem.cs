namespace Kalshi.Integration.Contracts.Reliability;

/// <summary>
/// Represents an acquired outbox message ready for dispatch.
/// </summary>
public sealed record OutboxDispatchItem(
    Guid MessageId,
    Guid AggregateId,
    string AggregateType,
    string PayloadJson,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LeaseExpiresAt,
    string? LastError,
    OutboxMessageStatus Status);
