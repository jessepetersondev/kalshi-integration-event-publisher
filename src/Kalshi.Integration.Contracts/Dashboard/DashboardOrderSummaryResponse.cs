namespace Kalshi.Integration.Contracts.Dashboard;

/// <summary>
/// Represents a response payload for dashboard order summary.
/// </summary>


public sealed record DashboardOrderSummaryResponse(
    Guid Id,
    string Ticker,
    string Side,
    int Quantity,
    decimal LimitPrice,
    string StrategyName,
    string Status,
    int FilledQuantity,
    DateTimeOffset UpdatedAt);
