using System.Text.Json.Serialization;

namespace Kalshi.Integration.Contracts.Orders;

/// <summary>
/// Represents a response payload for order.
/// </summary>
[method: JsonConstructor]
public sealed record OrderResponse(
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
    string DecisionReason,
    string CommandSchemaVersion,
    string? TargetPositionTicker,
    string? TargetPositionSide,
    Guid? TargetPublisherOrderId,
    string? TargetClientOrderId,
    string? TargetExternalOrderId,
    string Status,
    string PublishStatus,
    string? LastResultStatus,
    string? LastResultMessage,
    string? ExternalOrderId,
    string? ClientOrderId,
    Guid? CommandEventId,
    int FilledQuantity,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<OrderEventResponse> Events,
    IReadOnlyList<OrderLifecycleEventResponse> LifecycleEvents)
{
    public OrderResponse(
        Guid id,
        Guid tradeIntentId,
        string ticker,
        string? side,
        int? quantity,
        decimal? limitPrice,
        string strategyName,
        string status,
        int filledQuantity,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        IReadOnlyList<OrderEventResponse> events)
        : this(
            id,
            tradeIntentId,
            ticker,
            side,
            quantity,
            limitPrice,
            strategyName,
            id.ToString("N"),
            "entry",
            "legacy-client",
            "legacy request",
            "weather-quant-command.v1",
            null,
            null,
            null,
            null,
            null,
            status,
            "publish_confirmed",
            null,
            null,
            null,
            null,
            null,
            filledQuantity,
            createdAt,
            updatedAt,
            events,
            Array.Empty<OrderLifecycleEventResponse>())
    {
    }
}

/// <summary>
/// Represents a response payload for order event.
/// </summary>
public sealed record OrderEventResponse(
    string Status,
    int FilledQuantity,
    DateTimeOffset OccurredAt);
