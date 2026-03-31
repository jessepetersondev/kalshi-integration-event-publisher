namespace Kalshi.Integration.Contracts.Orders;

/// <summary>
/// Represents a response payload for order.
/// </summary>
public sealed record OrderResponse(
    Guid Id,
    Guid TradeIntentId,
    string Ticker,
    string Side,
    int Quantity,
    decimal LimitPrice,
    string StrategyName,
    string Status,
    int FilledQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderEventResponse> Events);

/// <summary>
/// Represents a response payload for order event.
/// </summary>
public sealed record OrderEventResponse(
    string Status,
    int FilledQuantity,
    DateTimeOffset OccurredAt);
