namespace Kalshi.Integration.Contracts.TradeIntents;

/// <summary>
/// Represents a response payload for trade intent.
/// </summary>


public sealed record TradeIntentResponse(
    Guid Id,
    string Ticker,
    string Side,
    int Quantity,
    decimal LimitPrice,
    string StrategyName,
    string CorrelationId,
    DateTimeOffset CreatedAt,
    RiskDecisionResponse RiskDecision);
