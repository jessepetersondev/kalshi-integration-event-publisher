namespace Kalshi.Integration.Contracts.Orders;

/// <summary>
/// Represents a request payload for create order.
/// </summary>


public sealed record CreateOrderRequest(Guid TradeIntentId);
