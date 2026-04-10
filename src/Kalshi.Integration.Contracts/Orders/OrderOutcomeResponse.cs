namespace Kalshi.Integration.Contracts.Orders;

/// <summary>
/// Represents a publisher-owned execution outcome view for a tracked order.
/// </summary>
public sealed record OrderOutcomeResponse(
    Guid Id,
    Guid TradeIntentId,
    string Ticker,
    string? Side,
    int? Quantity,
    decimal? LimitPrice,
    string StrategyName,
    string CorrelationId,
    string ActionType,
    string OriginService,
    string Status,
    string PublishStatus,
    string OutcomeState,
    string? LastResultStatus,
    string? LastResultMessage,
    string? ExternalOrderId,
    int FilledQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
