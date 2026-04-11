using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Integrations;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Contracts.TradeIntents;
using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;
using Microsoft.Extensions.Options;
using Moq;

namespace Kalshi.Integration.UnitTests;

public sealed class TradingServiceTests
{
    [Fact]
    public async Task CreateTradeIntentAsync_ShouldPersistTradeIntentAndReturnRiskDecisionResponse()
    {
        TradeIntent? capturedTradeIntent = null;
        Mock<ITradeIntentRepository> tradeIntentRepository = new(MockBehavior.Strict);
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        tradeIntentRepository
            .Setup(x => x.GetTradeIntentByCorrelationIdAsync("corr-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TradeIntent?)null);
        tradeIntentRepository
            .Setup(x => x.AddTradeIntentAsync(It.IsAny<TradeIntent>(), It.IsAny<CancellationToken>()))
            .Callback<TradeIntent, CancellationToken>((tradeIntent, _) => capturedTradeIntent = tradeIntent)
            .Returns(Task.CompletedTask);

        TradingService service = CreateService(tradeIntentRepository.Object, orderRepository.Object, positionSnapshotRepository.Object, maxOrderSize: 5);

        TradeIntentResponse response = await service.CreateTradeIntentAsync(new CreateTradeIntentRequest(" kxbtc ", "yes", 2, 0.45678m, " Breakout ", "corr-1"));

        Assert.NotNull(capturedTradeIntent);
        Assert.Equal(capturedTradeIntent!.Id, response.Id);
        Assert.Equal("KXBTC", response.Ticker);
        Assert.Equal("yes", response.Side);
        Assert.Equal(2, response.Quantity);
        Assert.Equal(0.4568m, response.LimitPrice);
        Assert.Equal("Breakout", response.StrategyName);
        Assert.Equal("corr-1", response.CorrelationId);
        Assert.True(response.RiskDecision.Accepted);
        tradeIntentRepository.VerifyAll();
    }

    [Fact]
    public async Task CreateTradeIntentAsync_ShouldThrowWhenRiskEvaluatorRejectsRequest()
    {
        Mock<ITradeIntentRepository> tradeIntentRepository = new(MockBehavior.Strict);
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        TradingService service = CreateService(tradeIntentRepository.Object, orderRepository.Object, positionSnapshotRepository.Object, maxOrderSize: 1);

        Task<TradeIntentResponse> action() => service.CreateTradeIntentAsync(new CreateTradeIntentRequest("KXBTC", "yes", 2, 0.40m, "Breakout", null));

        DomainException exception = await Assert.ThrowsAsync<DomainException>((Func<Task<TradeIntentResponse>>)action);
        Assert.Contains("max order size", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldPersistOrderArtifactsAndReturnMappedResponse()
    {
        TradeIntent tradeIntent = new("KXBTC", TradeSide.Yes, 2, 0.45m, "Breakout", "corr-order");
        Order? capturedOrder = null;
        PositionSnapshot? capturedPosition = null;
        List<ExecutionEvent> events = [];
        List<(string Stage, string? Details, DateTimeOffset OccurredAt)> lifecycleEvents = [];
        Mock<ITradeIntentRepository> tradeIntentRepository = new(MockBehavior.Strict);
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);

        tradeIntentRepository
            .Setup(x => x.GetTradeIntentAsync(tradeIntent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tradeIntent);
        orderRepository
            .Setup(x => x.GetLatestOrderByTradeIntentIdAsync(tradeIntent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);
        orderRepository
            .Setup(x => x.AddOrderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .Callback<Order, CancellationToken>((order, _) => capturedOrder = order)
            .Returns(Task.CompletedTask);
        orderRepository
            .Setup(x => x.AddOrderEventAsync(It.IsAny<ExecutionEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ExecutionEvent, CancellationToken>((executionEvent, _) => events.Add(executionEvent))
            .Returns(Task.CompletedTask);
        orderRepository
            .Setup(x => x.AddOrderLifecycleEventAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, string, string?, DateTimeOffset, CancellationToken>((_, stage, details, occurredAt, _) => lifecycleEvents.Add((stage, details, occurredAt)))
            .Returns(Task.CompletedTask);
        positionSnapshotRepository
            .Setup(x => x.UpsertPositionSnapshotAsync(It.IsAny<PositionSnapshot>(), It.IsAny<CancellationToken>()))
            .Callback<PositionSnapshot, CancellationToken>((snapshot, _) => capturedPosition = snapshot)
            .Returns(Task.CompletedTask);
        orderRepository
            .Setup(x => x.GetOrderEventsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid orderId, CancellationToken _) => (IReadOnlyList<ExecutionEvent>)events.Where(x => x.OrderId == orderId).ToArray());
        orderRepository
            .Setup(x => x.GetOrderLifecycleEventsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, CancellationToken _) => (IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)>)[.. lifecycleEvents]);

        TradingService service = CreateService(tradeIntentRepository.Object, orderRepository.Object, positionSnapshotRepository.Object);

        OrderResponse response = await service.CreateOrderAsync(new CreateOrderRequest(tradeIntent.Id));

        Assert.NotNull(capturedOrder);
        Assert.Equal(capturedOrder!.Id, response.Id);
        Assert.Equal(tradeIntent.Id, response.TradeIntentId);
        Assert.Equal("pending", response.Status);
        Assert.Equal(0, response.FilledQuantity);
        Assert.Single(response.Events);
        Assert.Equal("pending", response.Events[0].Status);
        Assert.NotNull(capturedPosition);
        Assert.Equal("KXBTC", capturedPosition!.Ticker);
        Assert.Equal(TradeSide.Yes, capturedPosition.Side);
        Assert.Equal(0, capturedPosition.Contracts);
        tradeIntentRepository.VerifyAll();
        orderRepository.VerifyAll();
        positionSnapshotRepository.VerifyAll();
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldThrowWhenTradeIntentDoesNotExist()
    {
        Mock<ITradeIntentRepository> tradeIntentRepository = new(MockBehavior.Strict);
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        tradeIntentRepository
            .Setup(x => x.GetTradeIntentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TradeIntent?)null);

        TradingService service = CreateService(tradeIntentRepository.Object, orderRepository.Object, positionSnapshotRepository.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateOrderAsync(new CreateOrderRequest(Guid.NewGuid())));
        tradeIntentRepository.VerifyAll();
    }

    [Fact]
    public async Task CreateOrderAsync_ShouldRejectDuplicateOrderForSameTradeIntent()
    {
        TradeIntent tradeIntent = new("KXBTC", TradeSide.Yes, 2, 0.45m, "Breakout", "corr-order");
        Order existingOrder = new(tradeIntent);
        Mock<ITradeIntentRepository> tradeIntentRepository = new(MockBehavior.Strict);
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);

        tradeIntentRepository
            .Setup(x => x.GetTradeIntentAsync(tradeIntent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tradeIntent);
        orderRepository
            .Setup(x => x.GetLatestOrderByTradeIntentIdAsync(tradeIntent.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        TradingService service = CreateService(tradeIntentRepository.Object, orderRepository.Object, positionSnapshotRepository.Object);

        DomainException exception = await Assert.ThrowsAsync<DomainException>(() => service.CreateOrderAsync(new CreateOrderRequest(tradeIntent.Id)));

        Assert.Contains("already has an order", exception.Message, StringComparison.OrdinalIgnoreCase);
        tradeIntentRepository.VerifyAll();
        orderRepository.VerifyAll();
    }

    [Fact]
    public async Task ApplyExecutionUpdateAsync_ShouldUpdateOrderAndPositionAndReturnMappedResponse()
    {
        TradeIntent tradeIntent = new("KXBTC", TradeSide.No, 3, 0.61m, "Fade", "corr-exec");
        Order order = new(tradeIntent);
        order.TransitionTo(OrderStatus.Accepted, updatedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        DateTimeOffset firstEventTime = DateTimeOffset.UtcNow.AddMinutes(-4);
        DateTimeOffset updateTime = DateTimeOffset.UtcNow.AddMinutes(-1);
        List<ExecutionEvent> events =
        [
            new(order.Id, OrderStatus.Accepted, 0, firstEventTime)
        ];
        PositionSnapshot? capturedPosition = null;
        Mock<ITradeIntentRepository> tradeIntentRepository = new(MockBehavior.Strict);
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);

        orderRepository
            .Setup(x => x.GetOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        orderRepository
            .Setup(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        orderRepository
            .Setup(x => x.AddOrderEventAsync(It.IsAny<ExecutionEvent>(), It.IsAny<CancellationToken>()))
            .Callback<ExecutionEvent, CancellationToken>((executionEvent, _) => events.Add(executionEvent))
            .Returns(Task.CompletedTask);
        positionSnapshotRepository
            .Setup(x => x.UpsertPositionSnapshotAsync(It.IsAny<PositionSnapshot>(), It.IsAny<CancellationToken>()))
            .Callback<PositionSnapshot, CancellationToken>((snapshot, _) => capturedPosition = snapshot)
            .Returns(Task.CompletedTask);
        orderRepository
            .Setup(x => x.GetOrderEventsAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ExecutionEvent>)events);
        orderRepository
            .Setup(x => x.GetOrderLifecycleEventsAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string Stage, string? Details, DateTimeOffset OccurredAt)>());

        TradingService service = CreateService(tradeIntentRepository.Object, orderRepository.Object, positionSnapshotRepository.Object);

        ExecutionUpdateResult result = await service.ApplyExecutionUpdateAsync(new ExecutionUpdateRequest(order.Id, "partially-filled", 2, updateTime, "corr-exec"));

        Assert.Equal(order.Id, result.OrderId);
        Assert.Equal("partiallyfilled", result.Status);
        Assert.Equal(2, result.FilledQuantity);
        Assert.Equal(updateTime, result.OccurredAt);
        Assert.Equal("partiallyfilled", result.Order.Status);
        Assert.Equal(2, result.Order.FilledQuantity);
        Assert.Equal(2, result.Order.Events.Count);
        Assert.True(result.Order.Events[0].OccurredAt <= result.Order.Events[1].OccurredAt);
        Assert.NotNull(capturedPosition);
        Assert.Equal("KXBTC", capturedPosition!.Ticker);
        Assert.Equal(TradeSide.No, capturedPosition.Side);
        Assert.Equal(2, capturedPosition.Contracts);
        orderRepository.VerifyAll();
        positionSnapshotRepository.VerifyAll();
    }

    [Fact]
    public async Task ApplyExecutionUpdateAsync_ShouldThrowWhenOrderDoesNotExist()
    {
        Mock<ITradeIntentRepository> tradeIntentRepository = new(MockBehavior.Strict);
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        orderRepository
            .Setup(x => x.GetOrderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        TradingService service = CreateService(tradeIntentRepository.Object, orderRepository.Object, positionSnapshotRepository.Object);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ApplyExecutionUpdateAsync(new ExecutionUpdateRequest(Guid.NewGuid(), "filled", 1, DateTimeOffset.UtcNow, null)));
        orderRepository.VerifyAll();
    }

    [Fact]
    public async Task ApplyExecutionUpdateAsync_ShouldThrowForInvalidStatus()
    {
        Order order = new(new TradeIntent("KXBTC", TradeSide.Yes, 1, 0.44m, "Breakout"));
        Mock<ITradeIntentRepository> tradeIntentRepository = new(MockBehavior.Strict);
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        orderRepository
            .Setup(x => x.GetOrderAsync(order.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        TradingService service = CreateService(tradeIntentRepository.Object, orderRepository.Object, positionSnapshotRepository.Object);

        await Assert.ThrowsAsync<DomainException>(() => service.ApplyExecutionUpdateAsync(new ExecutionUpdateRequest(order.Id, "bad-status", 1, DateTimeOffset.UtcNow, null)));
        orderRepository.VerifyAll();
    }

    private static TradingService CreateService(
        ITradeIntentRepository tradeIntentRepository,
        IOrderRepository orderRepository,
        IPositionSnapshotRepository positionSnapshotRepository,
        int maxOrderSize = 10)
    {
        RiskEvaluator riskEvaluator = new(tradeIntentRepository, Options.Create(new RiskOptions { MaxOrderSize = maxOrderSize }));
        return new TradingService(tradeIntentRepository, orderRepository, positionSnapshotRepository, riskEvaluator);
    }
}
