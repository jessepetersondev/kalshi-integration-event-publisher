using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.UnitTests;

public sealed class TradingServiceResultProjectionTests
{
    [Fact]
    public async Task MarkOrderPublishLifecycleMethods_ShouldUpdateOrderAndRecordLifecycleStages()
    {
        var repository = new InMemoryTradingRepository();
        var service = CreateService(repository);
        var tradeIntent = new TradeIntent("KXBTC", TradeSide.Yes, 2, 0.45m, "Breakout", "corr-1");
        var order = new Order(tradeIntent);
        var attemptedAt = new DateTimeOffset(2026, 4, 4, 11, 0, 0, TimeSpan.Zero);
        var confirmedAt = attemptedAt.AddMinutes(1);
        var reviewAt = attemptedAt.AddMinutes(2);
        var firstCommandEventId = Guid.NewGuid();
        var secondCommandEventId = Guid.NewGuid();

        await repository.AddTradeIntentAsync(tradeIntent);
        await repository.AddOrderAsync(order);

        await service.MarkOrderPublishAttemptedAsync(order.Id, attemptedAt);
        await service.MarkOrderPublishConfirmedAsync(order.Id, firstCommandEventId, confirmedAt);
        await service.MarkOrderPublishPendingReviewAsync(order.Id, " awaiting broker confirm ", secondCommandEventId, reviewAt);

        var persistedOrder = await repository.GetOrderAsync(order.Id);
        var lifecycleEvents = await repository.GetOrderLifecycleEventsAsync(order.Id);

        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderPublishStatus.PublishPendingReview, persistedOrder!.PublishStatus);
        Assert.Equal(firstCommandEventId, persistedOrder.CommandEventId);
        Assert.Equal("awaiting broker confirm", persistedOrder.LastResultMessage);
        Assert.Equal(reviewAt, persistedOrder.UpdatedAt);
        Assert.Collection(
            lifecycleEvents,
            evt => Assert.Equal("publish_attempted", evt.Stage),
            evt =>
            {
                Assert.Equal("publish_confirmed", evt.Stage);
                Assert.Equal($"commandEventId={firstCommandEventId}", evt.Details);
            },
            evt =>
            {
                Assert.Equal("publish_pending_review", evt.Stage);
                Assert.Equal(" awaiting broker confirm ", evt.Details);
            });
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldProjectAcceptedResultAndPositionSnapshot()
    {
        var repository = new InMemoryTradingRepository();
        var service = CreateService(repository);
        var tradeIntent = new TradeIntent("KXBTC", TradeSide.Yes, 3, 0.45m, "Breakout", "corr-2");
        var order = new Order(tradeIntent);
        var commandEventId = Guid.NewGuid();
        var occurredAt = new DateTimeOffset(2026, 4, 4, 11, 5, 0, TimeSpan.Zero);
        var resultEvent = new ApplicationEventEnvelope(
            Guid.NewGuid(),
            "trading",
            "order.execution_succeeded",
            order.Id.ToString(),
            "corr-2",
            "idem-2",
            new Dictionary<string, string?>
            {
                ["orderStatus"] = "resting",
                ["filledQuantity"] = "1",
                ["externalOrderId"] = " external-1 ",
                ["clientOrderId"] = " client-1 ",
                ["commandEventId"] = commandEventId.ToString(),
            },
            occurredAt);

        await repository.AddTradeIntentAsync(tradeIntent);
        order.TransitionTo(OrderStatus.Accepted, 0, occurredAt.AddSeconds(-30));
        await repository.AddOrderAsync(order);

        var applied = await service.ApplyExecutorResultAsync(resultEvent);

        var persistedOrder = await repository.GetOrderAsync(order.Id);
        var executionEvents = await repository.GetOrderEventsAsync(order.Id);
        var lifecycleEvents = await repository.GetOrderLifecycleEventsAsync(order.Id);
        var positions = await repository.GetPositionsAsync();

        Assert.True(applied);
        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderStatus.Resting, persistedOrder!.CurrentStatus);
        Assert.Equal(1, persistedOrder.FilledQuantity);
        Assert.Equal("order.execution_succeeded", persistedOrder.LastResultStatus);
        Assert.Equal("external-1", persistedOrder.ExternalOrderId);
        Assert.Equal("client-1", persistedOrder.ClientOrderId);
        Assert.Equal(commandEventId, persistedOrder.CommandEventId);
        var executionEvent = Assert.Single(executionEvents);
        Assert.Equal(OrderStatus.Resting, executionEvent.Status);
        var lifecycleEvent = Assert.Single(lifecycleEvents);
        Assert.Equal("order.execution_succeeded", lifecycleEvent.Stage);
        var position = Assert.Single(positions);
        Assert.Equal("KXBTC", position.Ticker);
        Assert.Equal(TradeSide.Yes, position.Side);
        Assert.Equal(1, position.Contracts);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldMapCancelSuccessWithoutExplicitOrderStatus()
    {
        var repository = new InMemoryTradingRepository();
        var service = CreateService(repository);
        var tradeIntent = new TradeIntent(
            "KXBTC",
            null,
            null,
            null,
            "Cancel",
            TradeIntentActionType.Cancel,
            "kalshi-weather-quant",
            "cancel stale order",
            "weather-quant-command.v1",
            targetPublisherOrderId: Guid.NewGuid(),
            correlationId: "corr-3");
        var order = new Order(tradeIntent);
        var resultEvent = new ApplicationEventEnvelope(
            Guid.NewGuid(),
            "trading",
            "order.execution_succeeded",
            order.Id.ToString(),
            "corr-3",
            "idem-3",
            new Dictionary<string, string?>(),
            DateTimeOffset.UtcNow);

        await repository.AddTradeIntentAsync(tradeIntent);
        await repository.AddOrderAsync(order);

        var applied = await service.ApplyExecutorResultAsync(resultEvent);

        var persistedOrder = await repository.GetOrderAsync(order.Id);
        Assert.True(applied);
        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderStatus.Canceled, persistedOrder!.CurrentStatus);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldMapExecutedStatusToFilledAndInferFullQuantity()
    {
        var repository = new InMemoryTradingRepository();
        var service = CreateService(repository);
        var tradeIntent = new TradeIntent("KXWEATHER", TradeSide.Yes, 1, 0.10m, "Manual", "corr-executed");
        var order = new Order(tradeIntent);
        var resultEvent = new ApplicationEventEnvelope(
            Guid.NewGuid(),
            "executor",
            "order.execution_succeeded",
            order.Id.ToString(),
            "corr-executed",
            "idem-executed",
            new Dictionary<string, string?>
            {
                ["orderStatus"] = "executed",
                ["externalOrderId"] = "ext-1",
                ["clientOrderId"] = "client-1",
            },
            DateTimeOffset.UtcNow);

        await repository.AddTradeIntentAsync(tradeIntent);
        order.TransitionTo(OrderStatus.Accepted, 0, DateTimeOffset.UtcNow.AddSeconds(-30));
        await repository.AddOrderAsync(order);

        var applied = await service.ApplyExecutorResultAsync(resultEvent);

        var persistedOrder = await repository.GetOrderAsync(order.Id);
        var positions = await repository.GetPositionsAsync();

        Assert.True(applied);
        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderStatus.Filled, persistedOrder!.CurrentStatus);
        Assert.Equal(1, persistedOrder.FilledQuantity);
        Assert.Equal("ext-1", persistedOrder.ExternalOrderId);
        Assert.Equal("client-1", persistedOrder.ClientOrderId);
        var position = Assert.Single(positions);
        Assert.Equal(1, position.Contracts);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldProjectDeadLetterReasonAsRejected()
    {
        var repository = new InMemoryTradingRepository();
        var service = CreateService(repository);
        var tradeIntent = new TradeIntent("KXBTC", TradeSide.No, 1, 0.52m, "Fade", "corr-4");
        var order = new Order(tradeIntent);
        var resultEvent = new ApplicationEventEnvelope(
            Guid.NewGuid(),
            "trading",
            "order.dead_lettered",
            null,
            "corr-4",
            "idem-4",
            new Dictionary<string, string?>
            {
                ["publisherOrderId"] = order.Id.ToString(),
                ["deadLetterQueue"] = "executor.order.dlq",
            },
            DateTimeOffset.UtcNow);

        await repository.AddTradeIntentAsync(tradeIntent);
        await repository.AddOrderAsync(order);

        var applied = await service.ApplyExecutorResultAsync(resultEvent);

        var persistedOrder = await repository.GetOrderAsync(order.Id);
        Assert.True(applied);
        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderStatus.Rejected, persistedOrder!.CurrentStatus);
        Assert.Equal("executor.order.dlq", persistedOrder.LastResultMessage);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldIgnoreDuplicateResultEvents()
    {
        var repository = new InMemoryTradingRepository();
        var service = CreateService(repository);
        var tradeIntent = new TradeIntent("KXBTC", TradeSide.Yes, 2, 0.40m, "Trend", "corr-5");
        var order = new Order(tradeIntent);
        var resultEvent = new ApplicationEventEnvelope(
            Guid.NewGuid(),
            "trading",
            "order.execution_failed",
            order.Id.ToString(),
            "corr-5",
            "idem-5",
            new Dictionary<string, string?>
            {
                ["errorMessage"] = "broker rejected order",
            },
            DateTimeOffset.UtcNow);

        await repository.AddTradeIntentAsync(tradeIntent);
        await repository.AddOrderAsync(order);

        var firstApplied = await service.ApplyExecutorResultAsync(resultEvent);
        var secondApplied = await service.ApplyExecutorResultAsync(resultEvent);

        var executionEvents = await repository.GetOrderEventsAsync(order.Id);
        var lifecycleEvents = await repository.GetOrderLifecycleEventsAsync(order.Id);

        Assert.True(firstApplied);
        Assert.False(secondApplied);
        Assert.Single(executionEvents);
        Assert.Single(lifecycleEvents);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldRejectMissingPublisherOrderIdentity()
    {
        var repository = new InMemoryTradingRepository();
        var service = CreateService(repository);
        var resultEvent = new ApplicationEventEnvelope(
            Guid.NewGuid(),
            "trading",
            "order.execution_failed",
            null,
            "corr-6",
            "idem-6",
            new Dictionary<string, string?>(),
            DateTimeOffset.UtcNow);

        var exception = await Assert.ThrowsAsync<DomainException>(() => service.ApplyExecutorResultAsync(resultEvent));

        Assert.Contains("publisher order identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TradingService CreateService(InMemoryTradingRepository repository, int maxOrderSize = 10)
    {
        var riskEvaluator = new RiskEvaluator(repository, Options.Create(new RiskOptions { MaxOrderSize = maxOrderSize }));
        return new TradingService(repository, repository, repository, riskEvaluator);
    }
}
