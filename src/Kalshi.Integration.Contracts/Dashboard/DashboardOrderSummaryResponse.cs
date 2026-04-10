namespace Kalshi.Integration.Contracts.Dashboard;

/// <summary>
/// Represents a response payload for dashboard order summary.
/// </summary>
public sealed record DashboardOrderSummaryResponse(
    Guid Id,
    string Ticker,
    string? Side,
    int? Quantity,
    decimal? LimitPrice,
    string StrategyName,
    string Status,
    string PublishStatus,
    string? LastResultStatus,
    string CorrelationId,
    string ActionType,
    string? ExternalOrderId,
    int FilledQuantity,
    DateTimeOffset UpdatedAt);
