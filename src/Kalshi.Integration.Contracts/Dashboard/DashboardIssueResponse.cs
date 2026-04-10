namespace Kalshi.Integration.Contracts.Dashboard;

/// <summary>
/// Represents a response payload for dashboard issue.
/// </summary>
public sealed record DashboardIssueResponse(
    Guid Id,
    string Category,
    string Severity,
    string Source,
    string Message,
    string? Details,
    DateTimeOffset OccurredAt,
    string? CorrelationId = null);
