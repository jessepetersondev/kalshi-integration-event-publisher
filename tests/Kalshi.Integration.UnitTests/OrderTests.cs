using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.TradeIntents;

namespace Kalshi.Integration.UnitTests;

public sealed class OrderTests
{
    [Fact]
    public void Order_ShouldStartInPendingStatus()
    {
        var intent = new TradeIntent("KXBTC", TradeSide.Yes, 2, 0.45m, "Breakout");
        var order = new Order(intent);

        Assert.Equal(OrderStatus.Pending, order.CurrentStatus);
        Assert.Equal(0, order.FilledQuantity);
    }

    [Fact]
    public void SetPersistenceState_ShouldOverridePersistedFields()
    {
        var createdAt = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(5);
        var order = new Order(new TradeIntent("KXBTC", TradeSide.Yes, 2, 0.45m, "Breakout"));
        var id = Guid.NewGuid();

        order.SetPersistenceState(id, OrderStatus.Resting, 1, createdAt, updatedAt);

        Assert.Equal(id, order.Id);
        Assert.Equal(OrderStatus.Resting, order.CurrentStatus);
        Assert.Equal(1, order.FilledQuantity);
        Assert.Equal(createdAt, order.CreatedAt);
        Assert.Equal(updatedAt, order.UpdatedAt);
    }

    [Fact]
    public void TransitionTo_ShouldAllowPendingToAcceptedToRestingToPartiallyFilledToFilledToSettled()
    {
        var intent = new TradeIntent("KXBTC", TradeSide.Yes, 2, 0.45m, "Breakout");
        var order = new Order(intent);

        order.TransitionTo(OrderStatus.Accepted);
        order.TransitionTo(OrderStatus.Resting);
        order.TransitionTo(OrderStatus.PartiallyFilled, filledQuantity: 1);
        order.TransitionTo(OrderStatus.Filled, filledQuantity: 2);
        order.TransitionTo(OrderStatus.Settled);

        Assert.Equal(OrderStatus.Settled, order.CurrentStatus);
        Assert.Equal(2, order.FilledQuantity);
    }

    [Fact]
    public void TransitionTo_ShouldAllowPendingToDirectFill()
    {
        var intent = new TradeIntent("KXBTC", TradeSide.No, 1, 0.55m, "Fade");
        var order = new Order(intent);

        order.TransitionTo(OrderStatus.Filled, filledQuantity: 1);

        Assert.Equal(OrderStatus.Filled, order.CurrentStatus);
        Assert.Equal(1, order.FilledQuantity);
    }

    [Fact]
    public void TransitionTo_ShouldRejectIllegalStatusChanges()
    {
        var intent = new TradeIntent("KXBTC", TradeSide.No, 1, 0.55m, "Fade");
        var order = new Order(intent);

        Assert.Throws<DomainException>(() => order.TransitionTo(OrderStatus.Settled, filledQuantity: 1));
    }

    [Fact]
    public void TransitionTo_ShouldRejectNegativeFilledQuantity()
    {
        var order = CreateAcceptedOrder(quantity: 3);

        Assert.Throws<DomainException>(() => order.TransitionTo(OrderStatus.PartiallyFilled, filledQuantity: -1));
    }

    [Fact]
    public void TransitionTo_ShouldRejectFilledQuantityMovingBackward()
    {
        var order = CreateAcceptedOrder(quantity: 3);
        order.TransitionTo(OrderStatus.PartiallyFilled, filledQuantity: 2);

        Assert.Throws<DomainException>(() => order.TransitionTo(OrderStatus.PartiallyFilled, filledQuantity: 1));
    }

    [Fact]
    public void TransitionTo_ShouldRejectFilledQuantityExceedingOrderQuantity()
    {
        var order = CreateAcceptedOrder(quantity: 3);

        Assert.Throws<DomainException>(() => order.TransitionTo(OrderStatus.PartiallyFilled, filledQuantity: 4));
    }

    [Fact]
    public void TransitionTo_ShouldRejectFilledStatusWhenQuantityIsNotComplete()
    {
        var order = CreateAcceptedOrder(quantity: 3);

        Assert.Throws<DomainException>(() => order.TransitionTo(OrderStatus.Filled, filledQuantity: 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void TransitionTo_ShouldRejectPartiallyFilledWithoutPartialQuantity(int filledQuantity)
    {
        var order = CreateAcceptedOrder(quantity: 3);

        Assert.Throws<DomainException>(() => order.TransitionTo(OrderStatus.PartiallyFilled, filledQuantity));
    }

    [Fact]
    public void MarkPublishLifecycleMethods_ShouldUpdatePublishStateAndPreserveFirstCommandEventId()
    {
        var createdAt = new DateTimeOffset(2026, 4, 4, 10, 0, 0, TimeSpan.Zero);
        var firstCommandEventId = Guid.NewGuid();
        var secondCommandEventId = Guid.NewGuid();
        var order = new Order(new TradeIntent("KXBTC", TradeSide.Yes, 2, 0.40m, "Trend"));

        order.MarkPublishAttempted(createdAt);
        order.MarkPublishConfirmed(firstCommandEventId, createdAt.AddMinutes(1));
        order.MarkManualInterventionRequired("  pending broker confirmation  ", secondCommandEventId, createdAt.AddMinutes(2));

        Assert.Equal(OrderPublishStatus.ManualInterventionRequired, order.PublishStatus);
        Assert.Equal(firstCommandEventId, order.CommandEventId);
        Assert.Equal("pending broker confirmation", order.LastResultMessage);
        Assert.Equal(createdAt.AddMinutes(2), order.UpdatedAt);
    }

    [Fact]
    public void ApplyResult_ShouldUpdateMetadataWithoutChangingStatus()
    {
        var updatedAt = new DateTimeOffset(2026, 4, 4, 10, 15, 0, TimeSpan.Zero);
        var commandEventId = Guid.NewGuid();
        var order = CreateAcceptedOrder(quantity: 3);

        order.ApplyResult(
            " order.execution_succeeded ",
            filledQuantity: 2,
            lastResultMessage: " partially filled ",
            externalOrderId: " external-1 ",
            clientOrderId: " client-1 ",
            commandEventId: commandEventId,
            updatedAt: updatedAt);

        Assert.Equal(OrderStatus.Accepted, order.CurrentStatus);
        Assert.Equal(2, order.FilledQuantity);
        Assert.Equal("order.execution_succeeded", order.LastResultStatus);
        Assert.Equal("partially filled", order.LastResultMessage);
        Assert.Equal("external-1", order.ExternalOrderId);
        Assert.Equal("client-1", order.ClientOrderId);
        Assert.Equal(commandEventId, order.CommandEventId);
        Assert.Equal(updatedAt, order.UpdatedAt);
    }

    [Fact]
    public void ApplyResult_ShouldPreserveExistingMetadataWhenReplacementValuesAreBlank()
    {
        var existingCommandEventId = Guid.NewGuid();
        var replacementCommandEventId = Guid.NewGuid();
        var order = CreateAcceptedOrder(quantity: 3);
        order.ApplyResult(
            "order.execution_succeeded",
            filledQuantity: 1,
            lastResultMessage: "kept",
            externalOrderId: "external-1",
            clientOrderId: "client-1",
            commandEventId: existingCommandEventId);

        order.ApplyResult(
            "order.execution_failed",
            lastResultMessage: "   ",
            externalOrderId: "   ",
            clientOrderId: null,
            commandEventId: replacementCommandEventId);

        Assert.Equal("order.execution_failed", order.LastResultStatus);
        Assert.Equal("kept", order.LastResultMessage);
        Assert.Equal("external-1", order.ExternalOrderId);
        Assert.Equal("client-1", order.ClientOrderId);
        Assert.Equal(existingCommandEventId, order.CommandEventId);
    }

    [Fact]
    public void ApplyResult_ShouldRejectBlankResultStatus()
    {
        var order = CreateAcceptedOrder(quantity: 1);

        Assert.Throws<DomainException>(() => order.ApplyResult(" "));
    }

    private static Order CreateAcceptedOrder(int quantity)
    {
        var order = new Order(new TradeIntent("KXBTC", TradeSide.Yes, quantity, 0.40m, "Trend"));
        order.TransitionTo(OrderStatus.Accepted);
        return order;
    }
}
