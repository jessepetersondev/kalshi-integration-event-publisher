namespace Kalshi.Integration.Contracts.TradeIntents;

/// <summary>
/// Represents a request payload for create trade intent.
/// </summary>


public sealed record CreateTradeIntentRequest(
    string Ticker,
    string Side,
    int Quantity,
    decimal LimitPrice,
    string StrategyName,
    string? CorrelationId);
