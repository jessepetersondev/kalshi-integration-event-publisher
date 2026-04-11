namespace Kalshi.Integration.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents a consumed executor result event.
/// </summary>
public sealed class ResultEventEntity
{
    public Guid Id { get; set; }
    public Guid? OrderId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public int ApplyAttemptCount { get; set; }
    public DateTimeOffset? LastApplyAttemptAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
    public string? LastError { get; set; }
}
