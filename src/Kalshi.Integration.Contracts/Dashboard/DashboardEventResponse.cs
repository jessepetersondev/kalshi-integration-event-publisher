namespace Kalshi.Integration.Contracts.Dashboard;

/// <summary>
/// Represents a response payload for dashboard event.
/// </summary>


public sealed record DashboardEventResponse(
    Guid OrderId,
    string Ticker,
    string Status,
    int FilledQuantity,
    DateTimeOffset OccurredAt);
