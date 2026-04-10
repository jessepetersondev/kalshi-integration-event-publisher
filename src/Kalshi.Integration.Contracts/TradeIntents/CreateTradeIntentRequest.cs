namespace Kalshi.Integration.Contracts.TradeIntents;

/// <summary>
/// Represents a request payload for create trade intent.
/// </summary>
public sealed record CreateTradeIntentRequest(
    string Ticker,
    string? Side,
    int? Quantity,
    decimal? LimitPrice,
    string StrategyName,
    string? CorrelationId,
    string ActionType = "entry",
    string OriginService = "legacy-client",
    string DecisionReason = "legacy request",
    string CommandSchemaVersion = "weather-quant-command.v1",
    string? TargetPositionTicker = null,
    string? TargetPositionSide = null,
    Guid? TargetPublisherOrderId = null,
    string? TargetClientOrderId = null,
    string? TargetExternalOrderId = null);
