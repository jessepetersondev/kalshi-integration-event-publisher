using System.Text.Json.Serialization;

namespace Kalshi.Integration.Contracts.Kalshi;

/// <summary>
/// Represents the Kalshi-compatible order payload accepted by the publisher bridge.
/// </summary>
public sealed record SubmitKalshiOrderRequest(
    [property: JsonPropertyName("ticker")] string Ticker,
    [property: JsonPropertyName("client_order_id")] string ClientOrderId,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("time_in_force")] string TimeInForce,
    [property: JsonPropertyName("post_only")] bool PostOnly,
    [property: JsonPropertyName("cancel_order_on_pause")] bool CancelOrderOnPause,
    [property: JsonPropertyName("subaccount")] int? Subaccount,
    [property: JsonPropertyName("reduce_only")] bool? ReduceOnly,
    [property: JsonPropertyName("yes_price_dollars")] decimal? YesPriceDollars,
    [property: JsonPropertyName("no_price_dollars")] decimal? NoPriceDollars,
    [property: JsonPropertyName("strategy_name")] string? StrategyName = null,
    [property: JsonPropertyName("origin_service")] string? OriginService = null,
    [property: JsonPropertyName("decision_reason")] string? DecisionReason = null,
    [property: JsonPropertyName("command_schema_version")] string? CommandSchemaVersion = null,
    [property: JsonPropertyName("correlation_id")] string? CorrelationId = null);
