namespace Kalshi.Integration.Executor.Persistence.Entities;

public sealed class ExecutorOutboxMessageEntity
{
    public Guid Id { get; set; }
    public Guid ExecutionRecordId { get; set; }
    public string MessageType { get; set; } = string.Empty;
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
