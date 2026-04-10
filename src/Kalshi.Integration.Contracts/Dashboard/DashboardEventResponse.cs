namespace Kalshi.Integration.Contracts.Dashboard;

/// <summary>
/// Represents a response payload for dashboard event.
/// </summary>
public sealed record DashboardEventResponse(
    Guid OrderId,
    string Ticker,
    string Status,
    string Category,
    string? Details,
    int FilledQuantity,
    DateTimeOffset OccurredAt);
