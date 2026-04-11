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
        InMemoryTradingRepository repository = new();
        TradingService service = CreateService(repository);
        TradeIntent tradeIntent = new("KXBTC", TradeSide.Yes, 2, 0.45m, "Breakout", "corr-1");
        Order order = new(tradeIntent);
        DateTimeOffset attemptedAt = new(2026, 4, 4, 11, 0, 0, TimeSpan.Zero);
        DateTimeOffset confirmedAt = attemptedAt.AddMinutes(1);
        DateTimeOffset reviewAt = attemptedAt.AddMinutes(2);
        Guid firstCommandEventId = Guid.NewGuid();
        Guid secondCommandEventId = Guid.NewGuid();

        await repository.AddTradeIntentAsync(tradeIntent);
        await repository.AddOrderAsync(order);

        await service.MarkOrderPublishAttemptedAsync(order.Id, attemptedAt);
        await service.MarkOrderPublishConfirmedAsync(order.Id, firstCommandEventId, confirmedAt);
        await service.MarkOrderManualInterventionRequiredAsync(order.Id, " awaiting broker confirm ", secondCommandEventId, reviewAt);

        Order? persistedOrder = await repository.GetOrderAsync(order.Id);
        IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)> lifecycleEvents = await repository.GetOrderLifecycleEventsAsync(order.Id);

        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderPublishStatus.ManualInterventionRequired, persistedOrder!.PublishStatus);
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
                Assert.Equal("manual_intervention_required", evt.Stage);
                Assert.Equal(" awaiting broker confirm ", evt.Details);
            });
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldProjectAcceptedResultAndPositionSnapshot()
    {
        InMemoryTradingRepository repository = new();
        TradingService service = CreateService(repository);
        TradeIntent tradeIntent = new("KXBTC", TradeSide.Yes, 3, 0.45m, "Breakout", "corr-2");
        Order order = new(tradeIntent);
        Guid commandEventId = Guid.NewGuid();
        DateTimeOffset occurredAt = new(2026, 4, 4, 11, 5, 0, TimeSpan.Zero);
        ApplicationEventEnvelope resultEvent = new(
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

        bool applied = await service.ApplyExecutorResultAsync(resultEvent);

        Order? persistedOrder = await repository.GetOrderAsync(order.Id);
        IReadOnlyList<Domain.Executions.ExecutionEvent> executionEvents = await repository.GetOrderEventsAsync(order.Id);
        IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)> lifecycleEvents = await repository.GetOrderLifecycleEventsAsync(order.Id);
        IReadOnlyList<PositionSnapshot> positions = await repository.GetPositionsAsync();

        Assert.True(applied);
        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderStatus.Resting, persistedOrder!.CurrentStatus);
        Assert.Equal(1, persistedOrder.FilledQuantity);
        Assert.Equal("order.execution_succeeded", persistedOrder.LastResultStatus);
        Assert.Equal("external-1", persistedOrder.ExternalOrderId);
        Assert.Equal("client-1", persistedOrder.ClientOrderId);
        Assert.Equal(commandEventId, persistedOrder.CommandEventId);
        Domain.Executions.ExecutionEvent executionEvent = Assert.Single(executionEvents);
        Assert.Equal(OrderStatus.Resting, executionEvent.Status);
        (string Stage, _, _) = Assert.Single(lifecycleEvents);
        Assert.Equal("order.execution_succeeded", Stage);
        PositionSnapshot position = Assert.Single(positions);
        Assert.Equal("KXBTC", position.Ticker);
        Assert.Equal(TradeSide.Yes, position.Side);
        Assert.Equal(1, position.Contracts);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldMapCancelSuccessWithoutExplicitOrderStatus()
    {
        InMemoryTradingRepository repository = new();
        TradingService service = CreateService(repository);
        TradeIntent tradeIntent = new(
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
        Order order = new(tradeIntent);
        ApplicationEventEnvelope resultEvent = new(
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

        bool applied = await service.ApplyExecutorResultAsync(resultEvent);

        Order? persistedOrder = await repository.GetOrderAsync(order.Id);
        Assert.True(applied);
        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderStatus.Canceled, persistedOrder!.CurrentStatus);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldMapExecutedStatusToFilledAndInferFullQuantity()
    {
        InMemoryTradingRepository repository = new();
        TradingService service = CreateService(repository);
        TradeIntent tradeIntent = new("KXWEATHER", TradeSide.Yes, 1, 0.10m, "Manual", "corr-executed");
        Order order = new(tradeIntent);
        ApplicationEventEnvelope resultEvent = new(
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

        bool applied = await service.ApplyExecutorResultAsync(resultEvent);

        Order? persistedOrder = await repository.GetOrderAsync(order.Id);
        IReadOnlyList<PositionSnapshot> positions = await repository.GetPositionsAsync();

        Assert.True(applied);
        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderStatus.Filled, persistedOrder!.CurrentStatus);
        Assert.Equal(1, persistedOrder.FilledQuantity);
        Assert.Equal("ext-1", persistedOrder.ExternalOrderId);
        Assert.Equal("client-1", persistedOrder.ClientOrderId);
        PositionSnapshot position = Assert.Single(positions);
        Assert.Equal(1, position.Contracts);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldProjectDeadLetterReasonAsRejected()
    {
        InMemoryTradingRepository repository = new();
        TradingService service = CreateService(repository);
        TradeIntent tradeIntent = new("KXBTC", TradeSide.No, 1, 0.52m, "Fade", "corr-4");
        Order order = new(tradeIntent);
        ApplicationEventEnvelope resultEvent = new(
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

        bool applied = await service.ApplyExecutorResultAsync(resultEvent);

        Order? persistedOrder = await repository.GetOrderAsync(order.Id);
        Assert.True(applied);
        Assert.NotNull(persistedOrder);
        Assert.Equal(OrderStatus.Rejected, persistedOrder!.CurrentStatus);
        Assert.Equal("executor.order.dlq", persistedOrder.LastResultMessage);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldIgnoreDuplicateResultEvents()
    {
        InMemoryTradingRepository repository = new();
        TradingService service = CreateService(repository);
        TradeIntent tradeIntent = new("KXBTC", TradeSide.Yes, 2, 0.40m, "Trend", "corr-5");
        Order order = new(tradeIntent);
        ApplicationEventEnvelope resultEvent = new(
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

        bool firstApplied = await service.ApplyExecutorResultAsync(resultEvent);
        bool secondApplied = await service.ApplyExecutorResultAsync(resultEvent);

        IReadOnlyList<Domain.Executions.ExecutionEvent> executionEvents = await repository.GetOrderEventsAsync(order.Id);
        IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)> lifecycleEvents = await repository.GetOrderLifecycleEventsAsync(order.Id);

        Assert.True(firstApplied);
        Assert.False(secondApplied);
        Assert.Single(executionEvents);
        Assert.Single(lifecycleEvents);
    }

    [Fact]
    public async Task ApplyExecutorResultAsync_ShouldRejectMissingPublisherOrderIdentity()
    {
        InMemoryTradingRepository repository = new();
        TradingService service = CreateService(repository);
        ApplicationEventEnvelope resultEvent = new(
            Guid.NewGuid(),
            "trading",
            "order.execution_failed",
            null,
            "corr-6",
            "idem-6",
            new Dictionary<string, string?>(),
            DateTimeOffset.UtcNow);

        DomainException exception = await Assert.ThrowsAsync<DomainException>(() => service.ApplyExecutorResultAsync(resultEvent));

        Assert.Contains("publisher order identity", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static TradingService CreateService(InMemoryTradingRepository repository, int maxOrderSize = 10)
    {
        RiskEvaluator riskEvaluator = new(repository, Options.Create(new RiskOptions { MaxOrderSize = maxOrderSize }));
        return new TradingService(repository, repository, repository, riskEvaluator);
    }
}
