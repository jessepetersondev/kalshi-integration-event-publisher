using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.TradeIntents;

namespace Kalshi.Integration.UnitTests;

public sealed class TradeIntentTests
{
    [Fact]
    public void Constructor_ShouldNormalizeTickerAndPreserveBusinessData()
    {
        var intent = new TradeIntent(" kxbtc-yes ", TradeSide.Yes, 3, 0.54321m, " Mean Reversion ");

        Assert.Equal("KXBTC-YES", intent.Ticker);
        Assert.Equal(TradeSide.Yes, intent.Side);
        Assert.Equal(3, intent.Quantity);
        Assert.Equal(0.5432m, intent.LimitPrice);
        Assert.Equal("Mean Reversion", intent.StrategyName);
        Assert.False(string.IsNullOrWhiteSpace(intent.CorrelationId));
    }

    [Fact]
    public void Constructor_ShouldRespectExplicitCorrelationIdCreatedAtAndWithId()
    {
        var createdAt = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);
        var persistedId = Guid.NewGuid();

        var intent = new TradeIntent("KXBTC", TradeSide.No, 1, 0.40m, "Fade", " corr-1 ", createdAt)
            .WithId(persistedId);

        Assert.Equal(persistedId, intent.Id);
        Assert.Equal("corr-1", intent.CorrelationId);
        Assert.Equal(createdAt, intent.CreatedAt);
    }

    [Fact]
    public void Constructor_ShouldRejectBlankTicker()
    {
        Assert.Throws<DomainException>(() => new TradeIntent(" ", TradeSide.No, 1, 0.50m, "Test"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_ShouldRejectNonPositiveQuantity(int quantity)
    {
        Assert.Throws<DomainException>(() => new TradeIntent("KXBTC", TradeSide.No, quantity, 0.50m, "Test"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Constructor_ShouldRejectInvalidLimitPrice(decimal price)
    {
        Assert.Throws<DomainException>(() => new TradeIntent("KXBTC", TradeSide.No, 1, price, "Test"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_ShouldRejectBlankStrategyName(string strategyName)
    {
        Assert.Throws<DomainException>(() => new TradeIntent("KXBTC", TradeSide.No, 1, 0.50m, strategyName));
    }

    [Fact]
    public void Constructor_ShouldNormalizeMigratedActionFields()
    {
        var targetPublisherOrderId = Guid.NewGuid();

        var intent = new TradeIntent(
            " kxbtc ",
            TradeSide.Yes,
            2,
            0.43219m,
            " Weather Alpha ",
            TradeIntentActionType.Exit,
            " kalshi-weather-quant ",
            " unwind winner ",
            " weather-quant-command.v1 ",
            targetPositionTicker: " kxbtc ",
            targetPositionSide: TradeSide.No,
            targetPublisherOrderId: targetPublisherOrderId,
            targetClientOrderId: " client-1 ",
            targetExternalOrderId: " external-1 ",
            correlationId: " corr-1 ");

        Assert.Equal(TradeIntentActionType.Exit, intent.ActionType);
        Assert.Equal("KXBTC", intent.Ticker);
        Assert.Equal("Weather Alpha", intent.StrategyName);
        Assert.Equal("kalshi-weather-quant", intent.OriginService);
        Assert.Equal("unwind winner", intent.DecisionReason);
        Assert.Equal("weather-quant-command.v1", intent.CommandSchemaVersion);
        Assert.Equal("KXBTC", intent.TargetPositionTicker);
        Assert.Equal(TradeSide.No, intent.TargetPositionSide);
        Assert.Equal(targetPublisherOrderId, intent.TargetPublisherOrderId);
        Assert.Equal("client-1", intent.TargetClientOrderId);
        Assert.Equal("external-1", intent.TargetExternalOrderId);
        Assert.Equal("corr-1", intent.CorrelationId);
        Assert.Equal(0.4322m, intent.LimitPrice);
    }

    [Fact]
    public void Constructor_ShouldRejectExitWithoutTargetPosition()
    {
        Assert.Throws<DomainException>(() => new TradeIntent(
            "KXBTC",
            TradeSide.Yes,
            1,
            0.50m,
            "Exit",
            TradeIntentActionType.Exit,
            "kalshi-weather-quant",
            "take profit",
            "weather-quant-command.v1"));
    }

    [Fact]
    public void Constructor_ShouldRejectCancelWithoutTargetReference()
    {
        Assert.Throws<DomainException>(() => new TradeIntent(
            "KXBTC",
            null,
            null,
            null,
            "Cancel",
            TradeIntentActionType.Cancel,
            "kalshi-weather-quant",
            "cancel stale order",
            "weather-quant-command.v1"));
    }
}
