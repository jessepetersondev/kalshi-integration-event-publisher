using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Domain.Orders;

namespace Kalshi.Integration.Application.Trading;

internal static class OrderResponseFactory
{
    public static async Task<OrderResponse> CreateAsync(Order order, IOrderRepository orderRepository, CancellationToken cancellationToken)
    {
        IReadOnlyList<Domain.Executions.ExecutionEvent> events = await orderRepository.GetOrderEventsAsync(order.Id, cancellationToken);
        IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)> lifecycleEvents = await orderRepository.GetOrderLifecycleEventsAsync(order.Id, cancellationToken);

        return new OrderResponse(
            order.Id,
            order.TradeIntent.Id,
            order.TradeIntent.Ticker,
            order.TradeIntent.Side?.ToString().ToLowerInvariant(),
            order.TradeIntent.Quantity,
            order.TradeIntent.LimitPrice,
            order.TradeIntent.StrategyName,
            order.TradeIntent.CorrelationId,
            order.TradeIntent.ActionType.ToString().ToLowerInvariant(),
            order.TradeIntent.OriginService,
            order.TradeIntent.DecisionReason,
            order.TradeIntent.CommandSchemaVersion,
            order.TradeIntent.TargetPositionTicker,
            order.TradeIntent.TargetPositionSide?.ToString().ToLowerInvariant(),
            order.TradeIntent.TargetPublisherOrderId,
            order.TradeIntent.TargetClientOrderId,
            order.TradeIntent.TargetExternalOrderId,
            order.CurrentStatus.ToString().ToLowerInvariant(),
            order.PublishStatus.ToString().ToLowerInvariant(),
            order.LastResultStatus,
            order.LastResultMessage,
            order.ExternalOrderId,
            order.ClientOrderId,
            order.CommandEventId,
            order.FilledQuantity,
            order.CreatedAt,
            order.UpdatedAt,
            events
                .OrderBy(e => e.OccurredAt)
                .Select(e => new OrderEventResponse(e.Status.ToString().ToLowerInvariant(), e.FilledQuantity, e.OccurredAt))
                .ToArray(),
            lifecycleEvents
                .OrderBy(e => e.OccurredAt)
                .Select(e => new OrderLifecycleEventResponse(e.Stage, e.Details, e.OccurredAt))
                .ToArray());
    }
}
