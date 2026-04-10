using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Contracts.TradeIntents;

namespace Kalshi.Integration.UnitTests;

public sealed class WeatherQuantCommandMapperTests
{
    [Fact]
    public void MapTradeIntentAttributes_ShouldIncludeMigratedWeatherQuantFields()
    {
        var tradeIntentId = Guid.NewGuid();
        var targetPublisherOrderId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 4, 4, 10, 0, 0, TimeSpan.Zero);
        var response = new TradeIntentResponse(
            tradeIntentId,
            "KXBTC",
            "yes",
            3,
            0.4567m,
            "breakout",
            "corr-1",
            "exit",
            "kalshi-weather-quant",
            "take profit",
            "weather-quant-command.v1",
            "KXBTC",
            "yes",
            targetPublisherOrderId,
            "client-1",
            "external-1",
            createdAt,
            new RiskDecisionResponse(true, "accepted", Array.Empty<string>(), 10, false));

        var attributes = WeatherQuantCommandMapper.MapTradeIntentAttributes(response);

        Assert.Equal("weather-quant-command.v1", attributes["commandSchemaVersion"]);
        Assert.Equal("kalshi-weather-quant", attributes["originService"]);
        Assert.Equal("exit", attributes["actionType"]);
        Assert.Equal(tradeIntentId.ToString(), attributes["tradeIntentId"]);
        Assert.Null(attributes["publisherOrderId"]);
        Assert.Equal("KXBTC", attributes["ticker"]);
        Assert.Equal("yes", attributes["side"]);
        Assert.Equal("3", attributes["quantity"]);
        Assert.Equal("0.4567", attributes["limitPrice"]);
        Assert.Equal("breakout", attributes["strategyName"]);
        Assert.Equal("take profit", attributes["decisionReason"]);
        Assert.Equal("KXBTC", attributes["targetPositionTicker"]);
        Assert.Equal("yes", attributes["targetPositionSide"]);
        Assert.Equal(targetPublisherOrderId.ToString(), attributes["targetPublisherOrderId"]);
        Assert.Equal("client-1", attributes["targetClientOrderId"]);
        Assert.Equal("external-1", attributes["targetExternalOrderId"]);
    }

    [Fact]
    public void CreateTradeIntentEvent_ShouldUseCanonicalEnvelopeMetadata()
    {
        var response = new TradeIntentResponse(
            Guid.NewGuid(),
            "KXETH",
            null,
            null,
            null,
            "risk-reduction",
            "corr-2",
            "cancel",
            "kalshi-weather-quant",
            "operator cancel",
            "weather-quant-command.v1",
            null,
            null,
            null,
            "client-2",
            "external-2",
            DateTimeOffset.UtcNow,
            new RiskDecisionResponse(true, "accepted", Array.Empty<string>(), 10, false));

        var envelope = WeatherQuantCommandMapper.CreateTradeIntentEvent(response, "corr-2", "idem-2");

        Assert.Equal("trading", envelope.Category);
        Assert.Equal("trade-intent.created", envelope.Name);
        Assert.Equal(response.Id.ToString(), envelope.ResourceId);
        Assert.Equal("corr-2", envelope.CorrelationId);
        Assert.Equal("idem-2", envelope.IdempotencyKey);
        Assert.Equal("cancel", envelope.Attributes["actionType"]);
        Assert.Equal("client-2", envelope.Attributes["targetClientOrderId"]);
        Assert.Equal("external-2", envelope.Attributes["targetExternalOrderId"]);
    }

    [Fact]
    public void MapOrderAttributes_ShouldIncludePublisherOrderIdentity()
    {
        var orderId = Guid.NewGuid();
        var tradeIntentId = Guid.NewGuid();
        var targetPublisherOrderId = Guid.NewGuid();
        var commandEventId = Guid.NewGuid();
        var response = new OrderResponse(
            orderId,
            tradeIntentId,
            "KXBTC",
            "no",
            2,
            0.42m,
            "fade",
            "corr-3",
            "entry",
            "kalshi-weather-quant",
            "reversion",
            "weather-quant-command.v1",
            "KXBTC",
            "no",
            targetPublisherOrderId,
            "client-3",
            "external-3",
            "accepted",
            "publish_confirmed",
            "order.execution_succeeded",
            "accepted by executor",
            "ext-order",
            "client-order",
            commandEventId,
            1,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow,
            new[] { new OrderEventResponse("accepted", 1, DateTimeOffset.UtcNow) },
            new[] { new OrderLifecycleEventResponse("publish_confirmed", null, DateTimeOffset.UtcNow) });

        var attributes = WeatherQuantCommandMapper.MapOrderAttributes(response);

        Assert.Equal("entry", attributes["actionType"]);
        Assert.Equal(tradeIntentId.ToString(), attributes["tradeIntentId"]);
        Assert.Equal(orderId.ToString(), attributes["publisherOrderId"]);
        Assert.Equal("KXBTC", attributes["ticker"]);
        Assert.Equal("no", attributes["side"]);
        Assert.Equal("2", attributes["quantity"]);
        Assert.Equal("0.42", attributes["limitPrice"]);
        Assert.Equal("fade", attributes["strategyName"]);
        Assert.Equal("reversion", attributes["decisionReason"]);
        Assert.Equal(targetPublisherOrderId.ToString(), attributes["targetPublisherOrderId"]);
    }

    [Fact]
    public void CreateOrderEvent_ShouldUseCanonicalEnvelopeMetadata()
    {
        var response = new OrderResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "KXBTC",
            "yes",
            1,
            0.51m,
            "breakout",
            "corr-4",
            "entry",
            "kalshi-weather-quant",
            "opening trade",
            "weather-quant-command.v1",
            null,
            null,
            null,
            null,
            null,
            "pending",
            "publish_attempted",
            null,
            null,
            null,
            null,
            null,
            0,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            Array.Empty<OrderEventResponse>(),
            Array.Empty<OrderLifecycleEventResponse>());

        var envelope = WeatherQuantCommandMapper.CreateOrderEvent(response, "corr-4", "idem-4");

        Assert.Equal("trading", envelope.Category);
        Assert.Equal("order.created", envelope.Name);
        Assert.Equal(response.Id.ToString(), envelope.ResourceId);
        Assert.Equal("corr-4", envelope.CorrelationId);
        Assert.Equal("idem-4", envelope.IdempotencyKey);
        Assert.Equal("entry", envelope.Attributes["actionType"]);
        Assert.Equal(response.TradeIntentId.ToString(), envelope.Attributes["tradeIntentId"]);
        Assert.Equal(response.Id.ToString(), envelope.Attributes["publisherOrderId"]);
    }
}
