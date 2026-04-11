namespace Kalshi.Integration.Executor.Persistence.Entities;

public sealed class ExecutorInboundMessageEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ResourceId { get; set; }
    public string? CorrelationId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public int ReceiveAttemptCount { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastReceivedAt { get; set; }
    public DateTimeOffset? HandledAt { get; set; }
    public string? LastError { get; set; }
}
