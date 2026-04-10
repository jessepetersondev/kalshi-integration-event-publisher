using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Contracts.TradeIntents;
using Kalshi.Integration.Domain.TradeIntents;
using Microsoft.Extensions.Options;
using Moq;

namespace Kalshi.Integration.UnitTests;

public sealed class RiskEvaluatorTests
{
    [Fact]
    public async Task EvaluateTradeIntent_ShouldAcceptValidTradeIntent()
    {
        var tradeIntentRepository = new Mock<ITradeIntentRepository>(MockBehavior.Strict);
        tradeIntentRepository
            .Setup(x => x.GetTradeIntentByCorrelationIdAsync("corr-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TradeIntent?)null);

        var evaluator = CreateEvaluator(tradeIntentRepository.Object, new RiskOptions { MaxOrderSize = 5 });

        var result = await evaluator.EvaluateTradeIntentAsync(new CreateTradeIntentRequest("KXBTC", "yes", 2, 0.45m, "Breakout", "corr-1"));

        Assert.True(result.Accepted);
        Assert.Equal("accepted", result.Decision);
        Assert.Empty(result.Reasons);
        Assert.False(result.DuplicateCorrelationIdDetected);
        tradeIntentRepository.VerifyAll();
    }

    [Fact]
    public async Task EvaluateTradeIntent_ShouldRejectInvalidInputCollection()
    {
        var tradeIntentRepository = new Mock<ITradeIntentRepository>(MockBehavior.Strict);
        var evaluator = CreateEvaluator(tradeIntentRepository.Object, new RiskOptions { MaxOrderSize = 3, RejectDuplicateCorrelationIds = false });

        var result = await evaluator.EvaluateTradeIntentAsync(new CreateTradeIntentRequest(" ", "maybe", 0, 1.5m, " ", null));

        Assert.False(result.Accepted);
        Assert.Equal("rejected", result.Decision);
        Assert.Contains("Ticker is required.", result.Reasons);
        Assert.Contains("Side must be either 'yes' or 'no'.", result.Reasons);
        Assert.Contains("Quantity must be greater than zero.", result.Reasons);
        Assert.Contains("Limit price must be greater than 0 and less than or equal to 1.", result.Reasons);
        Assert.Contains("Strategy name is required.", result.Reasons);
        tradeIntentRepository.Verify(
            x => x.GetTradeIntentByCorrelationIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvaluateTradeIntent_ShouldRejectOversizedOrders()
    {
        var tradeIntentRepository = new Mock<ITradeIntentRepository>(MockBehavior.Strict);
        var evaluator = CreateEvaluator(tradeIntentRepository.Object, new RiskOptions { MaxOrderSize = 3 });

        var result = await evaluator.EvaluateTradeIntentAsync(new CreateTradeIntentRequest("KXBTC", "yes", 4, 0.45m, "Breakout", null));

        Assert.False(result.Accepted);
        Assert.Contains(result.Reasons, reason => reason.Contains("max order size", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvaluateTradeIntent_ShouldRejectDuplicateCorrelationIds()
    {
        var existingIntent = new TradeIntent("KXBTC", TradeSide.Yes, 1, 0.40m, "Test", "dup-1");
        var tradeIntentRepository = new Mock<ITradeIntentRepository>(MockBehavior.Strict);
        tradeIntentRepository
            .Setup(x => x.GetTradeIntentByCorrelationIdAsync("dup-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingIntent);

        var evaluator = CreateEvaluator(tradeIntentRepository.Object, new RiskOptions());

        var result = await evaluator.EvaluateTradeIntentAsync(new CreateTradeIntentRequest("KXBTC", "no", 1, 0.60m, "Fade", "dup-1"));

        Assert.False(result.Accepted);
        Assert.True(result.DuplicateCorrelationIdDetected);
        Assert.Contains(result.Reasons, reason => reason.Contains("already been used", StringComparison.OrdinalIgnoreCase));
        tradeIntentRepository.VerifyAll();
    }

    [Fact]
    public async Task EvaluateTradeIntent_ShouldSkipDuplicateLookupWhenFeatureDisabled()
    {
        var tradeIntentRepository = new Mock<ITradeIntentRepository>(MockBehavior.Strict);
        var evaluator = CreateEvaluator(tradeIntentRepository.Object, new RiskOptions { RejectDuplicateCorrelationIds = false });

        var result = await evaluator.EvaluateTradeIntentAsync(new CreateTradeIntentRequest("KXBTC", "yes", 1, 0.32m, "Scalp", "corr-disabled"));

        Assert.True(result.Accepted);
        tradeIntentRepository.Verify(
            x => x.GetTradeIntentByCorrelationIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EvaluateTradeIntent_ShouldRejectDuplicateCancelForTargetPublisherOrderId()
    {
        var targetPublisherOrderId = Guid.NewGuid();
        var existingCancelIntent = new TradeIntent(
            "KXBTC",
            null,
            null,
            null,
            "Breakout",
            TradeIntentActionType.Cancel,
            "kalshi-btc-quant",
            "cancel stale order",
            "kalshi-btc-quant.bridge.v1",
            targetPublisherOrderId: targetPublisherOrderId,
            correlationId: "cancel-1");

        var tradeIntentRepository = new Mock<ITradeIntentRepository>(MockBehavior.Strict);
        tradeIntentRepository
            .Setup(x => x.GetTradeIntentByCorrelationIdAsync("cancel-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TradeIntent?)null);
        tradeIntentRepository
            .Setup(x => x.FindMatchingCancelTradeIntentAsync(targetPublisherOrderId, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingCancelIntent);

        var evaluator = CreateEvaluator(tradeIntentRepository.Object, new RiskOptions());

        var result = await evaluator.EvaluateTradeIntentAsync(new CreateTradeIntentRequest(
            "KXBTC",
            null,
            null,
            null,
            "Breakout",
            "cancel-2",
            "cancel",
            "kalshi-btc-quant",
            "cancel stale order",
            "kalshi-btc-quant.bridge.v1",
            null,
            null,
            targetPublisherOrderId));

        Assert.False(result.Accepted);
        Assert.Contains("A cancel request already exists for the target order.", result.Reasons);
        tradeIntentRepository.VerifyAll();
    }

    private static RiskEvaluator CreateEvaluator(ITradeIntentRepository tradeIntentRepository, RiskOptions options)
        => new(tradeIntentRepository, Options.Create(options));
}
