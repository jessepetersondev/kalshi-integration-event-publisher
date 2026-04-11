namespace Kalshi.Integration.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents a durable outbound publisher-command message waiting for broker publication.
/// </summary>
public sealed class PublisherOutboxMessageEntity
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public string AggregateType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ProcessorId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? LastError { get; set; }
    public string? LastFailureKind { get; set; }
}
