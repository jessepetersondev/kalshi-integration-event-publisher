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
        Order olderOrder = CreateOrder("KXBTC", TradeSide.Yes, OrderStatus.Accepted, 1, DateTimeOffset.UtcNow.AddMinutes(-10));
        Order newerOrder = CreateOrder("KXETH", TradeSide.No, OrderStatus.Filled, 2, DateTimeOffset.UtcNow.AddMinutes(-1));
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        Mock<IOperationalIssueStore> issueStore = new(MockBehavior.Strict);
        Mock<IAuditRecordStore> auditStore = new(MockBehavior.Strict);

        orderRepository
            .Setup(x => x.GetOrdersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { olderOrder, newerOrder });

        DashboardService service = new(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        IReadOnlyList<Contracts.Dashboard.DashboardOrderSummaryResponse> result = await service.GetOrdersAsync();

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
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        Mock<IOperationalIssueStore> issueStore = new(MockBehavior.Strict);
        Mock<IAuditRecordStore> auditStore = new(MockBehavior.Strict);

        positionSnapshotRepository
            .Setup(x => x.GetPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new PositionSnapshot("KXETH", TradeSide.No, 1, 0.25m, DateTimeOffset.UtcNow),
                new PositionSnapshot("KXBTC", TradeSide.Yes, 2, 0.45m, DateTimeOffset.UtcNow)
            });

        DashboardService service = new(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        IReadOnlyList<Contracts.Positions.PositionResponse> result = await service.GetPositionsAsync();

        Assert.Equal("KXBTC", result[0].Ticker);
        Assert.Equal("KXETH", result[1].Ticker);
        positionSnapshotRepository.VerifyAll();
    }

    [Fact]
    public async Task GetEventsAsync_ShouldAggregateSortAndLimitEvents()
    {
        Order olderOrder = CreateOrder("KXBTC", TradeSide.Yes, OrderStatus.Accepted, 0, DateTimeOffset.UtcNow.AddMinutes(-10));
        Order newerOrder = CreateOrder("KXETH", TradeSide.No, OrderStatus.Filled, 2, DateTimeOffset.UtcNow.AddMinutes(-1));
        ExecutionEvent olderEvent = new(olderOrder.Id, OrderStatus.Accepted, 0, DateTimeOffset.UtcNow.AddMinutes(-9));
        ExecutionEvent newestEvent = new(newerOrder.Id, OrderStatus.Filled, 2, DateTimeOffset.UtcNow.AddMinutes(-2));
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        Mock<IOperationalIssueStore> issueStore = new(MockBehavior.Strict);
        Mock<IAuditRecordStore> auditStore = new(MockBehavior.Strict);

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

        DashboardService service = new(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        IReadOnlyList<Contracts.Dashboard.DashboardEventResponse> result = await service.GetEventsAsync(limit: 1);

        Contracts.Dashboard.DashboardEventResponse evt = Assert.Single(result);
        Assert.Equal(newerOrder.Id, evt.OrderId);
        Assert.Equal("KXETH", evt.Ticker);
        Assert.Equal("filled", evt.Status);
        orderRepository.VerifyAll();
    }

    [Fact]
    public async Task GetIssuesAsync_ShouldForwardFiltersAndSortDescending()
    {
        OperationalIssue olderIssue = OperationalIssue.Create("validation", "warning", "risk", "Older", occurredAt: DateTimeOffset.UtcNow.AddHours(-3));
        OperationalIssue newerIssue = OperationalIssue.Create("validation", "error", "risk", "Newer", occurredAt: DateTimeOffset.UtcNow.AddMinutes(-10));
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        Mock<IOperationalIssueStore> issueStore = new(MockBehavior.Strict);
        Mock<IAuditRecordStore> auditStore = new(MockBehavior.Strict);

        issueStore
            .Setup(x => x.GetRecentAsync("validation", 12, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { olderIssue, newerIssue });

        DashboardService service = new(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        IReadOnlyList<Contracts.Dashboard.DashboardIssueResponse> result = await service.GetIssuesAsync("validation", 12);

        Assert.Equal(2, result.Count);
        Assert.Equal(newerIssue.Id, result[0].Id);
        Assert.Equal(olderIssue.Id, result[1].Id);
        issueStore.VerifyAll();
    }

    [Fact]
    public async Task GetAuditRecordsAsync_ShouldForwardFiltersAndMapResults()
    {
        AuditRecord record = AuditRecord.Create(
            category: "trading",
            action: "create-order",
            outcome: "accepted",
            correlationId: "corr-1",
            details: "Created.",
            idempotencyKey: "idem-1",
            resourceId: "order-1",
            occurredAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        Mock<IOrderRepository> orderRepository = new(MockBehavior.Strict);
        Mock<IPositionSnapshotRepository> positionSnapshotRepository = new(MockBehavior.Strict);
        Mock<IOperationalIssueStore> issueStore = new(MockBehavior.Strict);
        Mock<IAuditRecordStore> auditStore = new(MockBehavior.Strict);

        auditStore
            .Setup(x => x.GetRecentAsync("trading", 6, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { record });

        DashboardService service = new(orderRepository.Object, positionSnapshotRepository.Object, issueStore.Object, auditStore.Object);

        IReadOnlyList<Contracts.Dashboard.DashboardAuditRecordResponse> result = await service.GetAuditRecordsAsync("trading", 6, 25);

        Contracts.Dashboard.DashboardAuditRecordResponse auditRecord = Assert.Single(result);
        Assert.Equal(record.Id, auditRecord.Id);
        Assert.Equal("create-order", auditRecord.Action);
        Assert.Equal("idem-1", auditRecord.IdempotencyKey);
        auditStore.VerifyAll();
    }

    private static Order CreateOrder(string ticker, TradeSide side, OrderStatus status, int filledQuantity, DateTimeOffset updatedAt)
    {
        Order order = new(new TradeIntent(ticker, side, 3, 0.45m, "Strategy"));
        order.SetPersistenceState(order.Id, status, filledQuantity, updatedAt.AddMinutes(-5), updatedAt);
        return order;
    }
}
