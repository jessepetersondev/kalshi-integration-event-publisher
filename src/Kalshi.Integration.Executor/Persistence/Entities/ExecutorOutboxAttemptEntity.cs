namespace Kalshi.Integration.Executor.Persistence.Entities;

public sealed class ExecutorOutboxAttemptEntity
{
    public Guid Id { get; set; }
    public Guid MessageId { get; set; }
    public int AttemptNumber { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? FailureKind { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
