namespace Kalshi.Integration.Executor.Persistence.Entities;

public sealed class ExecutorOperationalIssueEntity
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
