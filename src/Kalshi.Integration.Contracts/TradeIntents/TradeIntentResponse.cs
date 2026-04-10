namespace Kalshi.Integration.Contracts.TradeIntents;

/// <summary>
/// Represents a response payload for trade intent.
/// </summary>
public sealed record TradeIntentResponse(
    Guid Id,
    string Ticker,
    string? Side,
    int? Quantity,
    decimal? LimitPrice,
    string StrategyName,
    string CorrelationId,
    string ActionType,
    string OriginService,
    string DecisionReason,
    string CommandSchemaVersion,
    string? TargetPositionTicker,
    string? TargetPositionSide,
    Guid? TargetPublisherOrderId,
    string? TargetClientOrderId,
    string? TargetExternalOrderId,
    DateTimeOffset CreatedAt,
    RiskDecisionResponse RiskDecision);
