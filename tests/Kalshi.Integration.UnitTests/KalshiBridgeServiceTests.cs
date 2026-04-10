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
        var (bridge, _, _, _, apiClient, _) = CreateBridge(subaccount: 17);
        var seriesPayload = JsonNode.Parse("""{"series":[{"ticker":"KXBTC"}]}""")!;
        var marketsPayload = JsonNode.Parse("""{"markets":[{"ticker":"KXBTC-BTC-25APR09"}]}""")!;
        var marketPayload = JsonNode.Parse("""{"market":{"ticker":"KXBTC-BTC-25APR09"}}""")!;
        var balancePayload = JsonNode.Parse("""{"balance":12345}""")!;
        var positionsPayload = JsonNode.Parse("""{"market_positions":[{"ticker":"KXBTC-BTC-25APR09"}]}""")!;

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
        var (bridge, repository, queryService, _, apiClient, publisher) = CreateBridge(subaccount: 17);

        var result = await bridge.PlaceOrderAsync(new SubmitKalshiOrderRequest(
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

        var orderNode = Assert.IsType<JsonObject>(result["order"]);
        var publisherOrderId = Guid.Parse(orderNode["order_id"]!.GetValue<string>());
        Assert.Equal(publisherOrderId.ToString(), orderNode["publisher_order_id"]!.GetValue<string>());
        Assert.Equal("client-1", orderNode["client_order_id"]!.GetValue<string>());
        Assert.Null(orderNode["external_order_id"]);
        Assert.Equal("pending", orderNode["status"]!.GetValue<string>());
        Assert.Equal(2, orderNode["initial_count_fp"]!.GetValue<int>());
        Assert.Equal(0, orderNode["fill_count_fp"]!.GetValue<int>());
        Assert.Equal(2, orderNode["remaining_count_fp"]!.GetValue<int>());
        Assert.Equal(0.4523m, orderNode["yes_price_dollars"]!.GetValue<decimal>());

        var order = await queryService.GetOrderAsync(publisherOrderId);
        Assert.NotNull(order);
        Assert.Equal("pending", order!.Status);
        Assert.Equal("publishconfirmed", order.PublishStatus);
        Assert.Null(order.ExternalOrderId);
        Assert.Null(order.ClientOrderId);

        var tradeIntent = await repository.GetTradeIntentByCorrelationIdAsync("client-1");
        Assert.NotNull(tradeIntent);
        Assert.Equal("Breakout", tradeIntent!.StrategyName);
        Assert.Equal("kalshi-btc-quant", tradeIntent.OriginService);
        Assert.Equal("Momentum breakout", tradeIntent.DecisionReason);

        var publishedEvents = publisher.GetPublishedEvents();
        var publishedEvent = Assert.Single(publishedEvents);
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
    public async Task PlaceOrderAsync_ShouldMarkOrderPendingReviewWhenPublishConfirmationFails()
    {
        var publisher = new Mock<IApplicationEventPublisher>(MockBehavior.Strict);
        publisher
            .Setup(x => x.PublishAsync(It.IsAny<ApplicationEventEnvelope>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PublishConfirmationException("publisher unavailable"));

        var (bridge, _, queryService, _, apiClient, _) = CreateBridge(applicationEventPublisher: publisher.Object);

        var created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        var publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());
        var order = await queryService.GetOrderAsync(publisherOrderId);

        Assert.NotNull(order);
        Assert.Equal("pending", order!.Status);
        Assert.Equal("publishpendingreview", order.PublishStatus);
        Assert.Equal("publisher unavailable", order.LastResultMessage);
        publisher.VerifyAll();
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetOrderAsync_ShouldReflectExecutorUpdatesWithoutCallingKalshi()
    {
        var (bridge, _, queryService, tradingService, apiClient, _) = CreateBridge();

        var created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        var publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        var applied = await tradingService.ApplyExecutorResultAsync(
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
        var refreshed = await bridge.GetOrderAsync(publisherOrderId);

        var refreshedOrderNode = Assert.IsType<JsonObject>(refreshed["order"]);
        Assert.Equal("filled", refreshedOrderNode["status"]!.GetValue<string>());
        Assert.Equal(2, refreshedOrderNode["fill_count_fp"]!.GetValue<int>());
        Assert.Equal(0, refreshedOrderNode["remaining_count_fp"]!.GetValue<int>());
        Assert.Equal("ext-1", refreshedOrderNode["external_order_id"]!.GetValue<string>());

        var order = await queryService.GetOrderAsync(publisherOrderId);
        Assert.NotNull(order);
        Assert.Equal("filled", order!.Status);
        Assert.Equal(2, order.FilledQuantity);
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReturnExistingTerminalOrderWithoutCallingKalshi()
    {
        var (bridge, _, queryService, tradingService, apiClient, publisher) = CreateBridge();

        var created = await bridge.PlaceOrderAsync(new SubmitKalshiOrderRequest(
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
        var publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        var applied = await tradingService.ApplyExecutorResultAsync(
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

        var canceled = await bridge.CancelOrderAsync(publisherOrderId);

        var canceledOrderNode = Assert.IsType<JsonObject>(canceled["order"]);
        Assert.Equal("filled", canceledOrderNode["status"]!.GetValue<string>());
        Assert.Equal("sell", canceledOrderNode["action"]!.GetValue<string>());
        Assert.Equal("no", canceledOrderNode["side"]!.GetValue<string>());

        var order = await queryService.GetOrderAsync(publisherOrderId);
        Assert.NotNull(order);
        Assert.Equal("filled", order!.Status);
        Assert.Single(publisher.GetPublishedEvents());
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldPublishCancelCommandAndReturnCanceledBridgeEnvelope()
    {
        var (bridge, _, _, _, apiClient, publisher) = CreateBridge();

        var created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        var publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        var canceled = await bridge.CancelOrderAsync(publisherOrderId);

        var canceledOrderNode = Assert.IsType<JsonObject>(canceled["order"]);
        Assert.Equal(publisherOrderId.ToString(), canceledOrderNode["order_id"]!.GetValue<string>());
        Assert.Equal("canceled", canceledOrderNode["status"]!.GetValue<string>());

        var publishedEvents = publisher.GetPublishedEvents();
        Assert.Equal(2, publishedEvents.Count);

        var cancelEvent = publishedEvents[1];
        Assert.Equal("order.created", cancelEvent.Name);
        Assert.Equal("cancel", cancelEvent.Attributes["actionType"]);
        Assert.Equal(publisherOrderId.ToString(), cancelEvent.Attributes["targetPublisherOrderId"]);
        Assert.Equal("client-1", cancelEvent.Attributes["targetClientOrderId"]);
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReuseCancelOrderWhenPublishConfirmationIsPendingReview()
    {
        var publisher = new Mock<IApplicationEventPublisher>(MockBehavior.Strict);
        publisher
            .SetupSequence(x => x.PublishAsync(It.IsAny<ApplicationEventEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .ThrowsAsync(new PublishConfirmationException("broker confirmation pending"));

        var (bridge, repository, queryService, _, apiClient, _) = CreateBridge(applicationEventPublisher: publisher.Object);

        var created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        var publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        await bridge.CancelOrderAsync(publisherOrderId);
        await bridge.CancelOrderAsync(publisherOrderId);

        var cancelOrders = (await repository.GetOrdersAsync())
            .Where(order => order.TradeIntent.ActionType == Domain.TradeIntents.TradeIntentActionType.Cancel)
            .ToArray();

        var cancelOrder = Assert.Single(cancelOrders);
        var cancelProjection = await queryService.GetOrderAsync(cancelOrder.Id);

        Assert.NotNull(cancelProjection);
        Assert.Equal("publishpendingreview", cancelProjection!.PublishStatus);
        Assert.Equal("broker confirmation pending", cancelProjection.LastResultMessage);
        publisher.Verify(x => x.PublishAsync(It.IsAny<ApplicationEventEnvelope>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOrderAsync_ShouldReuseDeadLetteredCancelOrderWithoutCreatingNewRetry()
    {
        var (bridge, repository, queryService, tradingService, apiClient, publisher) = CreateBridge();

        var created = await bridge.PlaceOrderAsync(CreateEntryRequest());
        var publisherOrderId = Guid.Parse(created["order"]!["order_id"]!.GetValue<string>());

        await bridge.CancelOrderAsync(publisherOrderId);

        var cancelOrder = Assert.Single((await repository.GetOrdersAsync())
            .Where(order => order.TradeIntent.ActionType == Domain.TradeIntents.TradeIntentActionType.Cancel));

        var applied = await tradingService.ApplyExecutorResultAsync(
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

        var cancelProjection = await queryService.GetOrderAsync(cancelOrder.Id);
        Assert.NotNull(cancelProjection);
        Assert.Equal("rejected", cancelProjection!.Status);
        Assert.Equal("order.dead_lettered", cancelProjection.LastResultStatus);
        Assert.Equal("executor.order.dlq", cancelProjection.LastResultMessage);
        Assert.Equal(2, publisher.GetPublishedEvents().Count);
        Assert.Single((await repository.GetOrdersAsync()).Where(order => order.TradeIntent.ActionType == Domain.TradeIntents.TradeIntentActionType.Cancel));
        apiClient.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PlaceOrderAsync_ShouldRejectInvalidSideBeforeCallingKalshi()
    {
        var (bridge, repository, _, _, apiClient, _) = CreateBridge();

        var exception = await Assert.ThrowsAsync<DomainException>(() => bridge.PlaceOrderAsync(new SubmitKalshiOrderRequest(
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
        var repository = new InMemoryTradingRepository();
        var riskEvaluator = new RiskEvaluator(repository, Options.Create(new RiskOptions { MaxOrderSize = maxOrderSize }));
        var tradingService = new TradingService(repository, repository, repository, riskEvaluator);
        var queryService = new TradingQueryService(repository, repository);
        var apiClient = new Mock<IKalshiApiClient>(MockBehavior.Strict);
        var publisher = applicationEventPublisher as InMemoryApplicationEventPublisher ?? new InMemoryApplicationEventPublisher();
        var bridge = new KalshiBridgeService(
            apiClient.Object,
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
