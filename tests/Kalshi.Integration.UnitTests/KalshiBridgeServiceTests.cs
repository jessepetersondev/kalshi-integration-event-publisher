using System.Text.Json.Nodes;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Kalshi;
using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Infrastructure.Integrations.Kalshi;
using Kalshi.Integration.Infrastructure.Messaging;
using Kalshi.Integration.Infrastructure.Operations;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Moq;

namespace Kalshi.Integration.UnitTests;

public sealed class KalshiBridgeServiceTests
{
    [Fact]
    public async Task MarketAndPortfolioQueries_ShouldDelegateToKalshiClient()
    {
        (KalshiBridgeService bridge, InMemoryTradingRepository _, TradingQueryService _, TradingService _, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher _) = CreateBridge(subaccount: 17);
        JsonNode seriesPayload = JsonNode.Parse("""{"series":[{"ticker":"KXBTC"}]}""")!;
        JsonNode marketsPayload = JsonNode.Parse("""{"markets":[{"ticker":"KXBTC-BTC-25APR09"}]}""")!;
        JsonNode marketPayload = JsonNode.Parse("""{"market":{"ticker":"KXBTC-BTC-25APR09"}}""")!;
        JsonNode balancePayload = JsonNode.Parse("""{"balance":12345}""")!;
        JsonNode positionsPayload = JsonNode.Parse("""{"market_positions":[{"ticker":"KXBTC-BTC-25APR09"}]}""")!;

        apiClient
            .Setup(x => x.GetSeriesAsync("crypto", It.Is<IReadOnlyList<string>>(tags => tags.SequenceEqual(new[] { "bitcoin", "usd" })), It.IsAny<CancellationToken>()))
            .ReturnsAsync(seriesPayload);
        apiClient
            .Setup(x => x.GetMarketsAsync("open", 250, "KXBTC", "cursor-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(marketsPayload);
        apiClient
            .Setup(x => x.GetMarketAsync("KXBTC-BTC-25APR09", It.IsAny<CancellationToken>()))
            .ReturnsAsync(marketPayload);
        apiClient
            .Setup(x => x.GetBalanceAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balancePayload);
        apiClient
            .Setup(x => x.GetPositionsAsync(17, It.IsAny<CancellationToken>()))
            .ReturnsAsync(positionsPayload);

        Assert.Same(seriesPayload, await bridge.GetSeriesAsync("crypto", ["bitcoin", "usd"]));
        Assert.Same(marketsPayload, await bridge.GetMarketsAsync("open", 250, "KXBTC", "cursor-1"));
        Assert.Same(marketPayload, await bridge.GetMarketAsync("KXBTC-BTC-25APR09"));
        Assert.Same(balancePayload, await bridge.GetBalanceAsync());
        Assert.Same(positionsPayload, await bridge.GetPositionsAsync());
        apiClient.VerifyAll();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldPublishOrderCommandAndReturnBridgeEnvelope()
    {
        (KalshiBridgeService bridge, InMemoryTradingRepository repository, TradingQueryService queryService, TradingService _, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher publisher) = CreateBridge(subaccount: 17);

        JsonNode result = await bridge.PlaceOrderAsync(new SubmitKalshiOrderRequest(
            "KXBTC",
            "client-1",
            "yes",
            "buy",
            2,
            "limit",
            "good_til_cancelled",
            true,
            false,
            null,
            null,
            0.4523m,
            null,
            "Breakout",
            "kalshi-btc-quant",
            "Momentum breakout",
            "kalshi-btc-quant.bridge.v1",
            "client-1"));

        JsonObject orderNode = Assert.IsType<JsonObject>(result["order"]);
        Guid publisherOrderId = Guid.Parse(orderNode["order_id"]!.GetValue<string>());
        Assert.Equal(publisherOrderId.ToString(), orderNode["publisher_order_id"]!.GetValue<string>());
        Assert.Equal("client-1", orderNode["client_order_id"]!.GetValue<string>());
        Assert.Null(orderNode["external_order_id"]);
        Assert.Equal("pending", orderNode["status"]!.GetValue<string>());
        Assert.Equal(2, orderNode["initial_count_fp"]!.GetValue<int>());
        Assert.Equal(0, orderNode["fill_count_fp"]!.GetValue<int>());
        Assert.Equal(2, orderNode["remaining_count_fp"]!.GetValue<int>());
        Assert.Equal(0.4523m, orderNode["yes_price_dollars"]!.GetValue<decimal>());

        Contracts.Orders.OrderResponse? order = await queryService.GetOrderAsync(publisherOrderId);
        Assert.NotNull(order);
        Assert.Equal("pending", order!.Status);
        Assert.Equal("publishconfirmed", order.PublishStatus);
        Assert.Null(order.ExternalOrderId);
        Assert.Equal("client-1", order.ClientOrderId);

        Domain.TradeIntents.TradeIntent? tradeIntent = await repository.GetTradeIntentByCorrelationIdAsync("client-1");
        Assert.NotNull(tradeIntent);
        Assert.Equal("Breakout", tradeIntent!.StrategyName);
        Assert.Equal("kalshi-btc-quant", tradeIntent.OriginService);
        Assert.Equal("Momentum breakout", tradeIntent.DecisionReason);

        IReadOnlyList<ApplicationEventEnvelope> publishedEvents = publisher.GetPublishedEvents();
        ApplicationEventEnvelope publishedEvent = Assert.Single(publishedEvents);
        Assert.Equal("order.created", publishedEvent.Name);
        Assert.Equal(publisherOrderId.ToString(), publishedEvent.ResourceId);
        Assert.Equal("client-1", publishedEvent.CorrelationId);
        Assert.Equal("KXBTC", publishedEvent.Attributes["ticker"]);
        Assert.Equal("yes", publishedEvent.Attributes["side"]);
        Assert.Equal("entry", publishedEvent.Attributes["actionType"]);
        Assert.Equal("2", publishedEvent.Attributes["quantity"]);
        Assert.Equal("0.4523", publishedEvent.Attributes["limitPrice"]);
        Assert.Equal("good_till_canceled", publishedEvent.Attributes["timeInForce"]);
        Assert.Equal("true", publishedEvent.Attributes["postOnly"]);
        Assert.Equal("false", publishedEvent.Attributes["cancelOrderOnPause"]);
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldScheduleRetryWhenPublishConfirmationFails()
    {
        Mock<IApplicationEventPublisher> publisher = new(MockBehavior.Strict);
        publisher
            .Setup(x => x.PublishAsync(It.IsAny<ApplicationEventEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PublishConfirmationException("publisher unavailable"));

        (KalshiBridgeService bridge, InMemoryTradingRepository _, TradingQueryService queryService, TradingService _, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher _) = CreateBridge(applicationEventPublisher: publisher.Object);

        JsonNode created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        Guid publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());
        Contracts.Orders.OrderResponse? order = await queryService.GetOrderAsync(publisherOrderId);

        Assert.NotNull(order);
        Assert.Equal("pending", order!.Status);
        Assert.Equal("retryscheduled", order.PublishStatus);
        Assert.Equal("publisher unavailable", order.LastResultMessage);
        publisher.VerifyAll();
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetOrderAsync_ShouldReflectExecutorUpdatesWithoutCallingKalshi()
    {
        (KalshiBridgeService bridge, InMemoryTradingRepository _, TradingQueryService queryService, TradingService tradingService, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher _) = CreateBridge();

        JsonNode created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        Guid publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        bool applied = await tradingService.ApplyExecutorResultAsync(
            ApplicationEventEnvelope.Create(
                category: "execution",
                name: "order.execution_succeeded",
                resourceId: publisherOrderId.ToString(),
                correlationId: "client-1",
                attributes: new Dictionary<string, string?>
                {
                    ["publisherOrderId"] = publisherOrderId.ToString(),
                    ["orderStatus"] = "executed",
                    ["filledQuantity"] = "2",
                    ["externalOrderId"] = "ext-1",
                    ["clientOrderId"] = "client-1",
                }));

        Assert.True(applied);
        JsonNode refreshed = await bridge.GetOrderAsync(publisherOrderId);

        JsonObject refreshedOrderNode = Assert.IsType<JsonObject>(refreshed["order"]);
        Assert.Equal("filled", refreshedOrderNode["status"]!.GetValue<string>());
        Assert.Equal(2, refreshedOrderNode["fill_count_fp"]!.GetValue<int>());
        Assert.Equal(0, refreshedOrderNode["remaining_count_fp"]!.GetValue<int>());
        Assert.Equal("ext-1", refreshedOrderNode["external_order_id"]!.GetValue<string>());

        Contracts.Orders.OrderResponse? order = await queryService.GetOrderAsync(publisherOrderId);
        Assert.NotNull(order);
        Assert.Equal("filled", order!.Status);
        Assert.Equal(2, order.FilledQuantity);
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReturnExistingTerminalOrderWithoutCallingKalshi()
    {
        (KalshiBridgeService bridge, InMemoryTradingRepository _, TradingQueryService queryService, TradingService tradingService, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher publisher) = CreateBridge();

        JsonNode created = await bridge.PlaceOrderAsync(new SubmitKalshiOrderRequest(
            "KXBTC",
            "client-2",
            "no",
            "sell",
            3,
            "limit",
            "good_til_cancelled",
            false,
            false,
            null,
            false,
            null,
            0.61m,
            "Fade",
            "kalshi-btc-quant",
            "Take profit",
            "kalshi-btc-quant.bridge.v1",
            "client-2"));
        Guid publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        bool applied = await tradingService.ApplyExecutorResultAsync(
            ApplicationEventEnvelope.Create(
                category: "execution",
                name: "order.execution_succeeded",
                resourceId: publisherOrderId.ToString(),
                correlationId: "client-2",
                attributes: new Dictionary<string, string?>
                {
                    ["publisherOrderId"] = publisherOrderId.ToString(),
                    ["orderStatus"] = "executed",
                    ["filledQuantity"] = "3",
                }));
        Assert.True(applied);

        JsonNode canceled = await bridge.CancelOrderAsync(publisherOrderId);

        JsonObject canceledOrderNode = Assert.IsType<JsonObject>(canceled["order"]);
        Assert.Equal("filled", canceledOrderNode["status"]!.GetValue<string>());
        Assert.Equal("sell", canceledOrderNode["action"]!.GetValue<string>());
        Assert.Equal("no", canceledOrderNode["side"]!.GetValue<string>());

        Contracts.Orders.OrderResponse? order = await queryService.GetOrderAsync(publisherOrderId);
        Assert.NotNull(order);
        Assert.Equal("filled", order!.Status);
        Assert.Single(publisher.GetPublishedEvents());
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldPublishCancelCommandAndReturnCanceledBridgeEnvelope()
    {
        (KalshiBridgeService bridge, InMemoryTradingRepository _, TradingQueryService _, TradingService _, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher publisher) = CreateBridge();

        JsonNode created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        Guid publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        JsonNode canceled = await bridge.CancelOrderAsync(publisherOrderId);

        JsonObject canceledOrderNode = Assert.IsType<JsonObject>(canceled["order"]);
        Assert.Equal(publisherOrderId.ToString(), canceledOrderNode["order_id"]!.GetValue<string>());
        Assert.Equal("canceled", canceledOrderNode["status"]!.GetValue<string>());

        IReadOnlyList<ApplicationEventEnvelope> publishedEvents = publisher.GetPublishedEvents();
        Assert.Equal(2, publishedEvents.Count);

        ApplicationEventEnvelope cancelEvent = publishedEvents[1];
        Assert.Equal("order.created", cancelEvent.Name);
        Assert.Equal("cancel", cancelEvent.Attributes["actionType"]);
        Assert.Equal(publisherOrderId.ToString(), cancelEvent.Attributes["targetPublisherOrderId"]);
        Assert.Equal("client-1", cancelEvent.Attributes["targetClientOrderId"]);
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReuseCancelOrderWhenPublishConfirmationIsRetryScheduled()
    {
        Mock<IApplicationEventPublisher> publisher = new(MockBehavior.Strict);
        publisher
            .SetupSequence(x => x.PublishAsync(It.IsAny<ApplicationEventEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new PublishConfirmationException("broker confirmation pending"));

        (KalshiBridgeService bridge, InMemoryTradingRepository repository, TradingQueryService queryService, TradingService _, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher _) = CreateBridge(applicationEventPublisher: publisher.Object);

        JsonNode created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        Guid publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        await bridge.CancelOrderAsync(publisherOrderId);
        await bridge.CancelOrderAsync(publisherOrderId);

        Domain.Orders.Order[] cancelOrders = (await repository.GetOrdersAsync())
            .Where(order => order.TradeIntent.ActionType == Domain.TradeIntents.TradeIntentActionType.Cancel)
            .ToArray();

        Domain.Orders.Order cancelOrder = Assert.Single(cancelOrders);
        Contracts.Orders.OrderResponse? cancelProjection = await queryService.GetOrderAsync(cancelOrder.Id);

        Assert.NotNull(cancelProjection);
        Assert.Equal("retryscheduled", cancelProjection!.PublishStatus);
        Assert.Equal("broker confirmation pending", cancelProjection.LastResultMessage);
        publisher.Verify(x => x.PublishAsync(It.IsAny<ApplicationEventEnvelope>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReuseDeadLetteredCancelOrderWithoutCreatingNewRetry()
    {
        (KalshiBridgeService bridge, InMemoryTradingRepository repository, TradingQueryService queryService, TradingService tradingService, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher publisher) = CreateBridge();

        JsonNode created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        Guid publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        await bridge.CancelOrderAsync(publisherOrderId);

        Domain.Orders.Order cancelOrder = Assert.Single((await repository.GetOrdersAsync())
            .Where(order => order.TradeIntent.ActionType == Domain.TradeIntents.TradeIntentActionType.Cancel));

        bool applied = await tradingService.ApplyExecutorResultAsync(
            ApplicationEventEnvelope.Create(
                category: "execution",
                name: "order.dead_lettered",
                resourceId: cancelOrder.Id.ToString(),
                correlationId: cancelOrder.TradeIntent.CorrelationId,
                attributes: new Dictionary<string, string?>
                {
                    ["publisherOrderId"] = cancelOrder.Id.ToString(),
                    ["deadLetterQueue"] = "executor.order.dlq",
                }));

        Assert.True(applied);

        await bridge.CancelOrderAsync(publisherOrderId);
        await bridge.CancelOrderAsync(publisherOrderId);

        Contracts.Orders.OrderResponse? cancelProjection = await queryService.GetOrderAsync(cancelOrder.Id);
        Assert.NotNull(cancelProjection);
        Assert.Equal("rejected", cancelProjection!.Status);
        Assert.Equal("order.dead_lettered", cancelProjection.LastResultStatus);
        Assert.Equal("executor.order.dlq", cancelProjection.LastResultMessage);
        Assert.Equal(2, publisher.GetPublishedEvents().Count);
        Assert.Single((await repository.GetOrdersAsync()).Where(order => order.TradeIntent.ActionType == Domain.TradeIntents.TradeIntentActionType.Cancel));
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReuseRejectedCancelOrderWithoutCreatingNewRetry()
    {
        (KalshiBridgeService bridge, InMemoryTradingRepository repository, TradingQueryService queryService, TradingService tradingService, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher publisher) = CreateBridge();

        JsonNode created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        Guid publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        await bridge.CancelOrderAsync(publisherOrderId);

        Domain.Orders.Order cancelOrder = Assert.Single((await repository.GetOrdersAsync())
            .Where(order => order.TradeIntent.ActionType == Domain.TradeIntents.TradeIntentActionType.Cancel));

        cancelOrder.TransitionTo(Domain.Orders.OrderStatus.Rejected, 0, DateTimeOffset.UtcNow);
        await repository.UpdateOrderAsync(cancelOrder);

        await bridge.CancelOrderAsync(publisherOrderId);
        await bridge.CancelOrderAsync(publisherOrderId);

        Contracts.Orders.OrderResponse? cancelProjection = await queryService.GetOrderAsync(cancelOrder.Id);
        Assert.NotNull(cancelProjection);
        Assert.Equal("rejected", cancelProjection!.Status);
        Assert.Null(cancelProjection.LastResultStatus);
        Assert.Equal(2, publisher.GetPublishedEvents().Count);
        Assert.Single((await repository.GetOrdersAsync()).Where(order => order.TradeIntent.ActionType == Domain.TradeIntents.TradeIntentActionType.Cancel));
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldRejectInvalidSideBeforeCallingKalshi()
    {
        (KalshiBridgeService bridge, InMemoryTradingRepository repository, TradingQueryService _, TradingService _, Mock<IKalshiApiClient> apiClient, InMemoryApplicationEventPublisher _) = CreateBridge();

        DomainException exception = await Assert.ThrowsAsync<DomainException>(() => bridge.PlaceOrderAsync(new SubmitKalshiOrderRequest(
            "KXBTC",
            "client-invalid",
            "maybe",
            "buy",
            1,
            "limit",
            "good_til_cancelled",
            false,
            false,
            null,
            null,
            0.44m,
            null,
            "Breakout",
            "kalshi-btc-quant",
            "Invalid side test",
            "kalshi-btc-quant.bridge.v1",
            "corr-invalid")));

        Assert.Equal("Side must be either 'yes' or 'no'.", exception.Message);
        Assert.Empty(await repository.GetOrdersAsync());
        apiClient.VerifyNoOtherCalls();
    }

    private static SubmitKalshiOrderRequest CreateEntryRequest()
        => new(
            "KXBTC",
            "client-1",
            "yes",
            "buy",
            2,
            "limit",
            "good_til_cancelled",
            true,
            false,
            null,
            null,
            0.4523m,
            null,
            "Breakout",
            "kalshi-btc-quant",
            "Momentum breakout",
            "kalshi-btc-quant.bridge.v1",
            "client-1");

    private static (
        KalshiBridgeService Bridge,
        InMemoryTradingRepository Repository,
        TradingQueryService QueryService,
        TradingService TradingService,
        Mock<IKalshiApiClient> ApiClient,
        InMemoryApplicationEventPublisher ApplicationEventPublisher)
        CreateBridge(int subaccount = 7, int maxOrderSize = 10, IApplicationEventPublisher? applicationEventPublisher = null)
    {
        InMemoryTradingRepository repository = new();
        RiskEvaluator riskEvaluator = new(repository, Options.Create(new RiskOptions { MaxOrderSize = maxOrderSize }));
        TradingService tradingService = new(repository, repository, repository, riskEvaluator);
        TradingQueryService queryService = new(repository, repository);
        Mock<IKalshiApiClient> apiClient = new(MockBehavior.Strict);
        InMemoryApplicationEventPublisher publisher = applicationEventPublisher as InMemoryApplicationEventPublisher ?? new InMemoryApplicationEventPublisher();
        KalshiBridgeService bridge = new(
            apiClient.Object,
            repository,
            repository,
            applicationEventPublisher ?? publisher,
            tradingService,
            queryService,
            Options.Create(new KalshiApiOptions
            {
                BaseUrl = "https://example.test",
                Subaccount = subaccount,
                UserAgent = "kalshi-bridge-tests",
            }));

        return (bridge, repository, queryService, tradingService, apiClient, publisher);
    }
}
