using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Dashboard;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;
using Moq;

namespace Kalshi.Integration.UnitTests;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task GetOrdersAsync_ShouldSortByUpdatedAtDescendingAndMapFields()
    {
        var olderOrder = CreateOrder("KXBTC", TradeSide.Yes, OrderStatus.Accepted, 1, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newerOrder = CreateOrder("KXETH", TradeSide.No, OrderStatus.Filled, 2, DateTimeOffset.UtcNow.AddMinutes(-1));
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);
        var issueStore = new Mock<IOperationalIssueStore>(MockBehavior.Strict);
        var auditStore = new Mock<IAuditRecordStore>(MockBehavior.Strict);

        orderRepository
            .Setup(x => x.GetOrdersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { olderOrder, newerOrder });

        var service = new DashboardService(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        var result = await service.GetOrdersAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(newerOrder.Id, result[0].Id);
        Assert.Equal("no", result[0].Side);
        Assert.Equal("filled", result[0].Status);
        Assert.Equal(olderOrder.Id, result[1].Id);
        orderRepository.VerifyAll();
    }

    [Fact]
    public async Task GetPositionsAsync_ShouldSortByTicker()
    {
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);
        var issueStore = new Mock<IOperationalIssueStore>(MockBehavior.Strict);
        var auditStore = new Mock<IAuditRecordStore>(MockBehavior.Strict);

        positionSnapshotRepository
            .Setup(x => x.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PositionSnapshot("KXETH", TradeSide.No, 1, 0.25m, DateTimeOffset.UtcNow),
                new PositionSnapshot("KXBTC", TradeSide.Yes, 2, 0.45m, DateTimeOffset.UtcNow)
            });

        var service = new DashboardService(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        var result = await service.GetPositionsAsync();

        Assert.Equal("KXBTC", result[0].Ticker);
        Assert.Equal("KXETH", result[1].Ticker);
        positionSnapshotRepository.VerifyAll();
    }

    [Fact]
    public async Task GetEventsAsync_ShouldAggregateSortAndLimitEvents()
    {
        var olderOrder = CreateOrder("KXBTC", TradeSide.Yes, OrderStatus.Accepted, 0, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newerOrder = CreateOrder("KXETH", TradeSide.No, OrderStatus.Filled, 2, DateTimeOffset.UtcNow.AddMinutes(-1));
        var olderEvent = new ExecutionEvent(olderOrder.Id, OrderStatus.Accepted, 0, DateTimeOffset.UtcNow.AddMinutes(-9));
        var newestEvent = new ExecutionEvent(newerOrder.Id, OrderStatus.Filled, 2, DateTimeOffset.UtcNow.AddMinutes(-2));
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);
        var issueStore = new Mock<IOperationalIssueStore>(MockBehavior.Strict);
        var auditStore = new Mock<IAuditRecordStore>(MockBehavior.Strict);

        orderRepository
            .Setup(x => x.GetOrdersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { olderOrder, newerOrder });
        orderRepository
            .Setup(x => x.GetOrderEventsAsync(olderOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { olderEvent });
        orderRepository
            .Setup(x => x.GetOrderEventsAsync(newerOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { newestEvent });
        orderRepository
            .Setup(x => x.GetOrderLifecycleEventsAsync(olderOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string Stage, string? Details, DateTimeOffset OccurredAt)>());
        orderRepository
            .Setup(x => x.GetOrderLifecycleEventsAsync(newerOrder.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<(string Stage, string? Details, DateTimeOffset OccurredAt)>());

        var service = new DashboardService(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        var result = await service.GetEventsAsync(limit: 1);

        var evt = Assert.Single(result);
        Assert.Equal(newerOrder.Id, evt.OrderId);
        Assert.Equal("KXETH", evt.Ticker);
        Assert.Equal("filled", evt.Status);
        orderRepository.VerifyAll();
    }

    [Fact]
    public async Task GetIssuesAsync_ShouldForwardFiltersAndSortDescending()
    {
        var olderIssue = OperationalIssue.Create("validation", "warning", "risk", "Older", occurredAt: DateTimeOffset.UtcNow.AddHours(-3));
        var newerIssue = OperationalIssue.Create("validation", "error", "risk", "Newer", occurredAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);
        var issueStore = new Mock<IOperationalIssueStore>(MockBehavior.Strict);
        var auditStore = new Mock<IAuditRecordStore>(MockBehavior.Strict);

        issueStore
            .Setup(x => x.GetRecentAsync("validation", 12, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { olderIssue, newerIssue });

        var service = new DashboardService(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        var result = await service.GetIssuesAsync("validation", 12);

        Assert.Equal(2, result.Count);
        Assert.Equal(newerIssue.Id, result[0].Id);
        Assert.Equal(olderIssue.Id, result[1].Id);
        issueStore.VerifyAll();
    }

    [Fact]
    public async Task GetAuditRecordsAsync_ShouldForwardFiltersAndMapResults()
    {
        var record = AuditRecord.Create(
            category: "trading",
            action: "create-order",
            outcome: "accepted",
            correlationId: "corr-1",
            details: "Created.",
            idempotencyKey: "idem-1",
            resourceId: "order-1",
            occurredAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var orderRepository = new Mock<IOrderRepository>(MockBehavior.Strict);
        var positionSnapshotRepository = new Mock<IPositionSnapshotRepository>(MockBehavior.Strict);
        var issueStore = new Mock<IOperationalIssueStore>(MockBehavior.Strict);
        var auditStore = new Mock<IAuditRecordStore>(MockBehavior.Strict);

        auditStore
            .Setup(x => x.GetRecentAsync("trading", 6, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { record });

        var service = new DashboardService(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        var result = await service.GetAuditRecordsAsync("trading", 6, 25);

        var auditRecord = Assert.Single(result);
        Assert.Equal(record.Id, auditRecord.Id);
        Assert.Equal("create-order", auditRecord.Action);
        Assert.Equal("idem-1", auditRecord.IdempotencyKey);
        auditStore.VerifyAll();
    }

    private static Order CreateOrder(string ticker, TradeSide side, OrderStatus status, int filledQuantity, DateTimeOffset updatedAt)
    {
        var order = new Order(new TradeIntent(ticker, side, 3, 0.45m, "Strategy"));
        order.SetPersistenceState(order.Id, status, filledQuantity, updatedAt.AddMinutes(-5), updatedAt);
        return order;
    }
}
