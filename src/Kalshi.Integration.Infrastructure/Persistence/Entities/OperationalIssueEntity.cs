namespace Kalshi.Integration.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents the persistence model for operational issue.
/// </summary>
public sealed class OperationalIssueEntity
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
