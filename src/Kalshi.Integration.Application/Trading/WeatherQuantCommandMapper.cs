using System.Globalization;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Contracts.TradeIntents;

namespace Kalshi.Integration.Application.Trading;

/// <summary>
/// Maps publisher resources into the canonical migrated command envelope attributes.
/// </summary>
public static class WeatherQuantCommandMapper
{
    public static IReadOnlyDictionary<string, string?> MapTradeIntentAttributes(TradeIntentResponse response)
    {
        return BuildCommonAttributes(
            response.ActionType,
            response.Ticker,
            response.Side,
            response.Quantity,
            response.LimitPrice,
            response.StrategyName,
            response.OriginService,
            response.DecisionReason,
            response.CommandSchemaVersion,
            response.TargetPositionTicker,
            response.TargetPositionSide,
            response.TargetPublisherOrderId,
            response.TargetClientOrderId,
            response.TargetExternalOrderId,
            response.Id,
            publisherOrderId: null);
    }

    public static IReadOnlyDictionary<string, string?> MapOrderAttributes(OrderResponse response)
    {
        return BuildCommonAttributes(
            response.ActionType,
            response.Ticker,
            response.Side,
            response.Quantity,
            response.LimitPrice,
            response.StrategyName,
            response.OriginService,
            response.DecisionReason,
            response.CommandSchemaVersion,
            response.TargetPositionTicker,
            response.TargetPositionSide,
            response.TargetPublisherOrderId,
            response.TargetClientOrderId,
            response.TargetExternalOrderId,
            response.TradeIntentId,
            response.Id);
    }

    private static Dictionary<string, string?> BuildCommonAttributes(
        string actionType,
        string ticker,
        string? side,
        int? quantity,
        decimal? limitPrice,
        string strategyName,
        string originService,
        string decisionReason,
        string commandSchemaVersion,
        string? targetPositionTicker,
        string? targetPositionSide,
        Guid? targetPublisherOrderId,
        string? targetClientOrderId,
        string? targetExternalOrderId,
        Guid tradeIntentId,
        Guid? publisherOrderId)
    {
        return new Dictionary<string, string?>
        {
            ["commandSchemaVersion"] = commandSchemaVersion,
            ["originService"] = originService,
            ["actionType"] = actionType,
            ["tradeIntentId"] = tradeIntentId.ToString(),
            ["publisherOrderId"] = publisherOrderId?.ToString(),
            ["ticker"] = ticker,
            ["side"] = side,
            ["quantity"] = quantity?.ToString(CultureInfo.InvariantCulture),
            ["limitPrice"] = limitPrice?.ToString(CultureInfo.InvariantCulture),
            ["strategyName"] = strategyName,
            ["decisionReason"] = decisionReason,
            ["targetPositionTicker"] = targetPositionTicker,
            ["targetPositionSide"] = targetPositionSide,
            ["targetPublisherOrderId"] = targetPublisherOrderId?.ToString(),
            ["targetClientOrderId"] = targetClientOrderId,
            ["targetExternalOrderId"] = targetExternalOrderId,
        };
    }

    public static ApplicationEventEnvelope CreateTradeIntentEvent(TradeIntentResponse response, string correlationId, string? idempotencyKey)
        => ApplicationEventEnvelope.Create(
            category: "trading",
            name: "trade-intent.created",
            resourceId: response.Id.ToString(),
            correlationId: correlationId,
            idempotencyKey: idempotencyKey,
            attributes: MapTradeIntentAttributes(response));

    public static ApplicationEventEnvelope CreateOrderEvent(OrderResponse response, string correlationId, string? idempotencyKey)
        => ApplicationEventEnvelope.Create(
            category: "trading",
            name: "order.created",
            resourceId: response.Id.ToString(),
            correlationId: correlationId,
            idempotencyKey: idempotencyKey,
            attributes: MapOrderAttributes(response));
}
