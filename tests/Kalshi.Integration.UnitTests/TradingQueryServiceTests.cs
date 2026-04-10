using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;
using Moq;

namespace Kalshi.Integration.UnitTests;

public sealed class TradingQueryServiceTests
{
    [Fact]
    public async Task GetOrderAsync_ShouldReturnNullWhenRepositoryMisses()
    {
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);
        orderRepository
            .Setup(x => x.GetOrderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var service = new TradingQueryService(orderRepository.Object, positionSnapshotRepository.Object);

        var result = await service.GetOrderAsync(Guid.NewGuid());

        Assert.Null(result);
        orderRepository.VerifyAll();
    }

    [Fact]
    public async Task GetOrderAsync_ShouldMapOrderAndSortEvents()
    {
        var tradeIntent = new TradeIntent("KXBTC", TradeSide.Yes, 2, 0.45m, "Breakout");
        var order = new Order(tradeIntent);
        var olderEvent = new ExecutionEvent(order.Id, OrderStatus.Accepted, 0, DateTimeOffset.UtcNow.AddMinutes(-2));
        var newerEvent = new ExecutionEvent(order.Id, OrderStatus.PartiallyFilled, 1, DateTimeOffset.UtcNow.AddMinutes(-1));
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);

        orderRepository
            .Setup(x => x.GetOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        orderRepository
            .Setup(x => x.GetOrderEventsAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { newerEvent, olderEvent });
        orderRepository
            .Setup(x => x.GetOrderLifecycleEventsAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string Stage, string? Details, DateTimeOffset OccurredAt)>());

        var service = new TradingQueryService(orderRepository.Object, positionSnapshotRepository.Object);

        var result = await service.GetOrderAsync(order.Id);

        Assert.NotNull(result);
        Assert.Equal(order.Id, result!.Id);
        Assert.Equal("pending", result.Status);
        Assert.Equal(2, result.Quantity);
        Assert.Equal(2, result.Events.Count);
        Assert.Equal("accepted", result.Events[0].Status);
        Assert.Equal("partiallyfilled", result.Events[1].Status);
        orderRepository.VerifyAll();
    }

    [Fact]
    public async Task GetPositionsAsync_ShouldMapRepositoryPositions()
    {
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);
        positionSnapshotRepository
            .Setup(x => x.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PositionSnapshot("KXETH", TradeSide.No, 1, 0.22m, DateTimeOffset.UtcNow),
                new PositionSnapshot("KXBTC", TradeSide.Yes, 2, 0.45m, DateTimeOffset.UtcNow)
            });

        var service = new TradingQueryService(orderRepository.Object, positionSnapshotRepository.Object);

        var result = await service.GetPositionsAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("KXBTC", result[0].Ticker);
        Assert.Equal("yes", result[0].Side);
        Assert.Equal("KXETH", result[1].Ticker);
        Assert.Equal("no", result[1].Side);
        positionSnapshotRepository.VerifyAll();
    }

    [Fact]
    public async Task GetOrderOutcomesAsync_ShouldMapAndSortExecutionOutcomes()
    {
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);
        var baseTime = new DateTimeOffset(2026, 4, 6, 14, 0, 0, TimeSpan.Zero);

        var awaitingOrder = CreateOrder(
            ticker: "KXBTC-AWAIT",
            correlationId: "corr-await",
            originService: "weather-quant",
            strategyName: "Await",
            quantity: 2,
            side: TradeSide.Yes,
            limitPrice: 0.41m);
        awaitingOrder.SetPersistenceState(
            awaitingOrder.Id,
            OrderStatus.Pending,
            OrderPublishStatus.PublishConfirmed,
            lastResultStatus: null,
            lastResultMessage: null,
            externalOrderId: null,
            clientOrderId: null,
            commandEventId: Guid.NewGuid(),
            filledQuantity: 0,
            createdAt: baseTime.AddMinutes(-30),
            updatedAt: baseTime.AddMinutes(-10));

        var failedOrder = CreateOrder(
            ticker: "KXBTC-FAIL",
            correlationId: "corr-fail",
            originService: "weather-quant",
            strategyName: "Fail",
            quantity: 1,
            side: TradeSide.No,
            limitPrice: 0.59m);
        failedOrder.SetPersistenceState(
            failedOrder.Id,
            OrderStatus.Rejected,
            OrderPublishStatus.PublishPendingReview,
            lastResultStatus: "order.execution_failed",
            lastResultMessage: "exchange rejected order",
            externalOrderId: "ext-fail-1",
            clientOrderId: "client-fail-1",
            commandEventId: Guid.NewGuid(),
            filledQuantity: 0,
            createdAt: baseTime.AddMinutes(-25),
            updatedAt: baseTime.AddMinutes(-2));

        var succeededOrder = CreateOrder(
            ticker: "KXBTC-SUCCEED",
            correlationId: "corr-success",
            originService: "executor-client",
            strategyName: "Succeed",
            quantity: 3,
            side: TradeSide.Yes,
            limitPrice: 0.38m);
        succeededOrder.SetPersistenceState(
            succeededOrder.Id,
            OrderStatus.Accepted,
            OrderPublishStatus.PublishConfirmed,
            lastResultStatus: "order.execution_succeeded",
            lastResultMessage: "submitted to broker",
            externalOrderId: "ext-success-1",
            clientOrderId: "client-success-1",
            commandEventId: Guid.NewGuid(),
            filledQuantity: 1,
            createdAt: baseTime.AddMinutes(-20),
            updatedAt: baseTime.AddMinutes(-1));

        orderRepository
            .Setup(x => x.GetOrdersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { awaitingOrder, failedOrder, succeededOrder });

        var service = new TradingQueryService(orderRepository.Object, positionSnapshotRepository.Object);

        var result = await service.GetOrderOutcomesAsync(limit: 10);

        Assert.Equal(3, result.Count);
        Assert.Equal(succeededOrder.Id, result[0].Id);
        Assert.Equal("succeeded", result[0].OutcomeState);
        Assert.Equal("executor-client", result[0].OriginService);
        Assert.Equal("order.execution_succeeded", result[0].LastResultStatus);
        Assert.Equal("ext-success-1", result[0].ExternalOrderId);

        Assert.Equal(failedOrder.Id, result[1].Id);
        Assert.Equal("failed", result[1].OutcomeState);
        Assert.Equal("exchange rejected order", result[1].LastResultMessage);

        Assert.Equal(awaitingOrder.Id, result[2].Id);
        Assert.Equal("awaiting_result", result[2].OutcomeState);
        Assert.Equal("publishconfirmed", result[2].PublishStatus);

        orderRepository.VerifyAll();
    }

    [Fact]
    public async Task GetOrderOutcomesAsync_ShouldApplyCaseInsensitiveNormalizedFilters()
    {
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);
        var baseTime = new DateTimeOffset(2026, 4, 6, 15, 0, 0, TimeSpan.Zero);

        var matchingOrder = CreateOrder(
            ticker: "KXBTC-MATCH",
            correlationId: "corr-match",
            originService: "weather-quant",
            strategyName: "Match",
            quantity: 4,
            side: TradeSide.Yes,
            limitPrice: 0.44m);
        matchingOrder.SetPersistenceState(
            matchingOrder.Id,
            OrderStatus.Rejected,
            OrderPublishStatus.PublishPendingReview,
            lastResultStatus: "order.execution_blocked",
            lastResultMessage: "risk block",
            externalOrderId: "ext-match-1",
            clientOrderId: "client-match-1",
            commandEventId: Guid.NewGuid(),
            filledQuantity: 0,
            createdAt: baseTime.AddMinutes(-20),
            updatedAt: baseTime.AddMinutes(-3));

        var nonMatchingOrder = CreateOrder(
            ticker: "KXBTC-OTHER",
            correlationId: "corr-other",
            originService: "legacy-client",
            strategyName: "Other",
            quantity: 2,
            side: TradeSide.No,
            limitPrice: 0.53m);
        nonMatchingOrder.SetPersistenceState(
            nonMatchingOrder.Id,
            OrderStatus.Pending,
            OrderPublishStatus.PublishConfirmed,
            lastResultStatus: null,
            lastResultMessage: null,
            externalOrderId: null,
            clientOrderId: null,
            commandEventId: Guid.NewGuid(),
            filledQuantity: 0,
            createdAt: baseTime.AddMinutes(-15),
            updatedAt: baseTime.AddMinutes(-2));

        orderRepository
            .Setup(x => x.GetOrdersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { matchingOrder, nonMatchingOrder });

        var service = new TradingQueryService(orderRepository.Object, positionSnapshotRepository.Object);

        var result = await service.GetOrderOutcomesAsync(
            correlationId: "CORR-MATCH",
            originService: "WEATHER-QUANT",
            status: "rejected",
            publishStatus: "publish_pending_review",
            outcomeState: "FAILED",
            resultStatus: "order.execution_blocked",
            limit: 10);

        var outcome = Assert.Single(result);
        Assert.Equal(matchingOrder.Id, outcome.Id);
        Assert.Equal("failed", outcome.OutcomeState);
        Assert.Equal("weather-quant", outcome.OriginService);

        orderRepository.VerifyAll();
    }

    private static Order CreateOrder(
        string ticker,
        string correlationId,
        string originService,
        string strategyName,
        int quantity,
        TradeSide side,
        decimal limitPrice)
    {
        var tradeIntent = new TradeIntent(
            ticker,
            side,
            quantity,
            limitPrice,
            strategyName,
            TradeIntentActionType.Entry,
            originService,
            "signal accepted",
            "weather-quant-command.v2",
            correlationId: correlationId,
            createdAt: new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero));

        return new Order(tradeIntent);
    }
}
