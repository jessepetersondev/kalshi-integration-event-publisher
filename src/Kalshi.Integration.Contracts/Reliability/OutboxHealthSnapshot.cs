namespace Kalshi.Integration.Contracts.Reliability;

/// <summary>
/// Summarizes operational outbox health for metrics and health checks.
/// </summary>
public sealed record OutboxHealthSnapshot(
    long PendingCount,
    long ManualInterventionCount,
    DateTimeOffset? OldestPendingCreatedAt);
