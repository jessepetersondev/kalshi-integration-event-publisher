namespace Kalshi.Integration.Application.Operations;

/// <summary>
/// Represents operational issue.
/// </summary>


public sealed record OperationalIssue(
    Guid Id,
    string Category,
    string Severity,
    string Source,
    string Message,
    string? Details,
    DateTimeOffset OccurredAt)
{
    public static OperationalIssue Create(
        string category,
        string severity,
        string source,
        string message,
        string? details = null,
        DateTimeOffset? occurredAt = null)
    {
        return new OperationalIssue(Guid.NewGuid(), category, severity, source, message, details, occurredAt ?? DateTimeOffset.UtcNow);
    }
}
