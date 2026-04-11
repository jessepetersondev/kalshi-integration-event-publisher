using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;

namespace Kalshi.Integration.UnitTests;

public sealed class ExecutionAndPositionTests
{
    [Fact]
    public void ExecutionEvent_ShouldCaptureOrderEventData()
    {
        Guid orderId = Guid.NewGuid();
        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;

        ExecutionEvent executionEvent = new(orderId, OrderStatus.PartiallyFilled, 1, occurredAt);

        Assert.Equal(orderId, executionEvent.OrderId);
        Assert.Equal(OrderStatus.PartiallyFilled, executionEvent.Status);
        Assert.Equal(1, executionEvent.FilledQuantity);
        Assert.Equal(occurredAt, executionEvent.OccurredAt);
    }

    [Fact]
    public void ExecutionEvent_ShouldRejectEmptyOrderId()
    {
        Assert.Throws<DomainException>(() => new ExecutionEvent(Guid.Empty, OrderStatus.Accepted, 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExecutionEvent_ShouldRejectNegativeFilledQuantity()
    {
        Assert.Throws<DomainException>(() => new ExecutionEvent(Guid.NewGuid(), OrderStatus.Accepted, -1, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ExecutionEvent_WithId_ShouldOverrideGeneratedId()
    {
        Guid persistedId = Guid.NewGuid();
        ExecutionEvent executionEvent = new ExecutionEvent(Guid.NewGuid(), OrderStatus.Accepted, 0, DateTimeOffset.UtcNow)
            .WithId(persistedId);

        Assert.Equal(persistedId, executionEvent.Id);
    }

    [Fact]
    public void PositionSnapshot_ShouldNormalizeTickerAndRoundAveragePrice()
    {
        PositionSnapshot snapshot = new(" kxbtc-26mar2808 ", TradeSide.No, 4, 0.65439m, DateTimeOffset.UtcNow);

        Assert.Equal("KXBTC-26MAR2808", snapshot.Ticker);
        Assert.Equal(TradeSide.No, snapshot.Side);
        Assert.Equal(4, snapshot.Contracts);
        Assert.Equal(0.6544m, snapshot.AveragePrice);
    }

    [Fact]
    public void PositionSnapshot_ShouldRejectBlankTicker()
    {
        Assert.Throws<DomainException>(() => new PositionSnapshot(" ", TradeSide.Yes, 1, 0.10m, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void PositionSnapshot_ShouldRejectNegativeContracts()
    {
        Assert.Throws<DomainException>(() => new PositionSnapshot("KXBTC", TradeSide.Yes, -1, 0.10m, DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void PositionSnapshot_ShouldRejectInvalidAveragePrice(decimal averagePrice)
    {
        Assert.Throws<DomainException>(() => new PositionSnapshot("KXBTC", TradeSide.Yes, 1, averagePrice, DateTimeOffset.UtcNow));
    }
}
