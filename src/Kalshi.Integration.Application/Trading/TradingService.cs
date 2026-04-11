using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Contracts.Integrations;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Contracts.TradeIntents;
using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;

namespace Kalshi.Integration.Application.Trading;

/// <summary>
/// Coordinates trade-intent creation, order creation, and execution-update processing
/// for the publisher application.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="TradingService"/> class.
/// </remarks>
/// <param name="tradeIntentRepository">The repository used to persist trade intents.</param>
/// <param name="orderRepository">The repository used to persist orders and execution events.</param>
/// <param name="positionSnapshotRepository">The repository used to update position snapshots.</param>
/// <param name="resultProjectionStore">The store used to apply executor result projections transactionally.</param>
/// <param name="riskEvaluator">The risk evaluator applied before creating a trade intent.</param>
public sealed class TradingService(
    ITradeIntentRepository tradeIntentRepository,
    IOrderRepository orderRepository,
    IPositionSnapshotRepository positionSnapshotRepository,
    IExecutorResultProjectionStore resultProjectionStore,
    RiskEvaluator riskEvaluator)
{
    private readonly ITradeIntentRepository _tradeIntentRepository = tradeIntentRepository;
    private readonly IOrderRepository _orderRepository = orderRepository;
    private readonly IPositionSnapshotRepository _positionSnapshotRepository = positionSnapshotRepository;
    private readonly IExecutorResultProjectionStore _resultProjectionStore = resultProjectionStore;
    private readonly RiskEvaluator _riskEvaluator = riskEvaluator;

    /// <summary>
    /// Initializes a new instance of the <see cref="TradingService"/> class.
    /// </summary>
    /// <param name="tradeIntentRepository">The repository used to persist trade intents.</param>
    /// <param name="orderRepository">The repository used to persist orders and execution events.</param>
    /// <param name="positionSnapshotRepository">The repository used to update position snapshots.</param>
    /// <param name="riskEvaluator">The risk evaluator applied before creating a trade intent.</param>
    public TradingService(
        ITradeIntentRepository tradeIntentRepository,
        IOrderRepository orderRepository,
        IPositionSnapshotRepository positionSnapshotRepository,
        RiskEvaluator riskEvaluator)
        : this(
            tradeIntentRepository,
            orderRepository,
            positionSnapshotRepository,
            orderRepository as IExecutorResultProjectionStore ?? UnsupportedResultProjectionStore.Instance,
            riskEvaluator)
    {
    }

    private sealed class UnsupportedResultProjectionStore : IExecutorResultProjectionStore
    {
        public static UnsupportedResultProjectionStore Instance { get; } = new();

        public Task<bool> ApplyExecutorResultAsync(ApplicationEventEnvelope resultEvent, ResultProjectionMutation mutation, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The configured order repository does not support transactional executor result projection.");
    }

    public async Task<TradeIntentResponse> CreateTradeIntentAsync(CreateTradeIntentRequest request, CancellationToken cancellationToken = default)
    {
        RiskDecision riskDecision = await _riskEvaluator.EvaluateTradeIntentAsync(request, cancellationToken);
        if (!riskDecision.Accepted)
        {
            throw new DomainException(string.Join(" ", riskDecision.Reasons));
        }

        TradeIntent tradeIntent = new(
            request.Ticker,
            ParseOptionalSide(request.Side),
            request.Quantity,
            request.LimitPrice,
            request.StrategyName,
            ParseActionType(request.ActionType),
            request.OriginService,
            request.DecisionReason,
            request.CommandSchemaVersion,
            request.TargetPositionTicker,
            ParseOptionalSide(request.TargetPositionSide),
            request.TargetPublisherOrderId,
            request.TargetClientOrderId,
            request.TargetExternalOrderId,
            request.CorrelationId);

        await _tradeIntentRepository.AddTradeIntentAsync(tradeIntent, cancellationToken);

        return new TradeIntentResponse(
            tradeIntent.Id,
            tradeIntent.Ticker,
            tradeIntent.Side?.ToString().ToLowerInvariant(),
            tradeIntent.Quantity,
            tradeIntent.LimitPrice,
            tradeIntent.StrategyName,
            tradeIntent.CorrelationId,
            tradeIntent.ActionType.ToString().ToLowerInvariant(),
            tradeIntent.OriginService,
            tradeIntent.DecisionReason,
            tradeIntent.CommandSchemaVersion,
            tradeIntent.TargetPositionTicker,
            tradeIntent.TargetPositionSide?.ToString().ToLowerInvariant(),
            tradeIntent.TargetPublisherOrderId,
            tradeIntent.TargetClientOrderId,
            tradeIntent.TargetExternalOrderId,
            tradeIntent.CreatedAt,
            new RiskDecisionResponse(
                riskDecision.Accepted,
                riskDecision.Decision,
                [.. riskDecision.Reasons],
                riskDecision.MaxOrderSize,
                riskDecision.DuplicateCorrelationIdDetected));
    }

    public async Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        TradeIntent? tradeIntent = await _tradeIntentRepository.GetTradeIntentAsync(request.TradeIntentId, cancellationToken) ?? throw new KeyNotFoundException($"Trade intent '{request.TradeIntentId}' was not found.");
        Order? existingOrder = await _orderRepository.GetLatestOrderByTradeIntentIdAsync(tradeIntent.Id, cancellationToken);
        if (existingOrder is not null)
        {
            throw new DomainException($"Trade intent '{request.TradeIntentId}' already has an order.");
        }

        Order order = new(tradeIntent);
        await _orderRepository.AddOrderAsync(order, cancellationToken);
        await _orderRepository.AddOrderEventAsync(new ExecutionEvent(order.Id, order.CurrentStatus, order.FilledQuantity, order.CreatedAt), cancellationToken);
        await _orderRepository.AddOrderLifecycleEventAsync(order.Id, "order_created", null, order.CreatedAt, cancellationToken);

        if (tradeIntent.Side.HasValue && tradeIntent.LimitPrice.HasValue)
        {
            await _positionSnapshotRepository.UpsertPositionSnapshotAsync(new PositionSnapshot(tradeIntent.Ticker, tradeIntent.Side.Value, 0, tradeIntent.LimitPrice.Value, order.UpdatedAt), cancellationToken);
        }

        return await OrderResponseFactory.CreateAsync(order, _orderRepository, cancellationToken);
    }

    public async Task MarkOrderPublishAttemptedAsync(Guid orderId, DateTimeOffset? occurredAt = null, CancellationToken cancellationToken = default)
    {
        Order order = await _orderRepository.GetOrderAsync(orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order '{orderId}' was not found.");

        DateTimeOffset at = occurredAt ?? DateTimeOffset.UtcNow;
        order.MarkPublishAttempted(at);
        await _orderRepository.UpdateOrderAsync(order, cancellationToken);
        await _orderRepository.AddOrderLifecycleEventAsync(order.Id, "publish_attempted", null, at, cancellationToken);
    }

    public async Task MarkOrderPublishConfirmedAsync(Guid orderId, Guid commandEventId, DateTimeOffset? occurredAt = null, CancellationToken cancellationToken = default)
    {
        Order order = await _orderRepository.GetOrderAsync(orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order '{orderId}' was not found.");

        DateTimeOffset at = occurredAt ?? DateTimeOffset.UtcNow;
        order.MarkPublishConfirmed(commandEventId, at);
        await _orderRepository.UpdateOrderAsync(order, cancellationToken);
        await _orderRepository.AddOrderLifecycleEventAsync(order.Id, "publish_confirmed", $"commandEventId={commandEventId}", at, cancellationToken);
    }

    public async Task MarkOrderRetryScheduledAsync(Guid orderId, string reason, Guid commandEventId, DateTimeOffset? occurredAt = null, CancellationToken cancellationToken = default)
    {
        Order order = await _orderRepository.GetOrderAsync(orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order '{orderId}' was not found.");

        DateTimeOffset at = occurredAt ?? DateTimeOffset.UtcNow;
        order.MarkRetryScheduled(reason, commandEventId, at);
        await _orderRepository.UpdateOrderAsync(order, cancellationToken);
        await _orderRepository.AddOrderLifecycleEventAsync(order.Id, "publish_retry_scheduled", reason, at, cancellationToken);
    }

    public async Task MarkOrderManualInterventionRequiredAsync(Guid orderId, string reason, Guid commandEventId, DateTimeOffset? occurredAt = null, CancellationToken cancellationToken = default)
    {
        Order order = await _orderRepository.GetOrderAsync(orderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Order '{orderId}' was not found.");

        DateTimeOffset at = occurredAt ?? DateTimeOffset.UtcNow;
        order.MarkManualInterventionRequired(reason, commandEventId, at);
        await _orderRepository.UpdateOrderAsync(order, cancellationToken);
        await _orderRepository.AddOrderLifecycleEventAsync(order.Id, "manual_intervention_required", reason, at, cancellationToken);
    }

    public Task MarkOrderPublishPendingReviewAsync(Guid orderId, string reason, Guid commandEventId, DateTimeOffset? occurredAt = null, CancellationToken cancellationToken = default)
        => MarkOrderManualInterventionRequiredAsync(orderId, reason, commandEventId, occurredAt, cancellationToken);

    public async Task<bool> ApplyExecutorResultAsync(ApplicationEventEnvelope resultEvent, CancellationToken cancellationToken = default)
    {
        Guid? orderId = ResolveOrderId(resultEvent);
        if (!orderId.HasValue)
        {
            throw new DomainException("Result event is missing publisher order identity.");
        }

        Order order = await _orderRepository.GetOrderAsync(orderId.Value, cancellationToken)
            ?? throw new KeyNotFoundException($"Order '{orderId.Value}' was not found.");

        OrderStatus? mappedStatus = MapResultToOrderStatus(resultEvent, order);
        int filledQuantity = TryGetInt(resultEvent.Attributes, "filledQuantity")
            ?? InferFilledQuantity(resultEvent, order, mappedStatus)
            ?? order.FilledQuantity;
        string? details = TryGetString(resultEvent.Attributes, "blockReason")
            ?? TryGetString(resultEvent.Attributes, "errorMessage")
            ?? TryGetString(resultEvent.Attributes, "deadLetterQueue");
        return await _resultProjectionStore.ApplyExecutorResultAsync(
            resultEvent,
            new ResultProjectionMutation(
                orderId.Value,
                resultEvent.Name,
                mappedStatus,
                filledQuantity,
                details,
                TryGetString(resultEvent.Attributes, "externalOrderId"),
                TryGetString(resultEvent.Attributes, "clientOrderId"),
                TryGetGuid(resultEvent.Attributes, "commandEventId") ?? order.CommandEventId,
                mappedStatus is OrderStatus.Accepted or OrderStatus.Resting or OrderStatus.PartiallyFilled or OrderStatus.Filled),
            cancellationToken);
    }

    public async Task<ExecutionUpdateResult> ApplyExecutionUpdateAsync(ExecutionUpdateRequest request, CancellationToken cancellationToken = default)
    {
        Order? order = await _orderRepository.GetOrderAsync(request.OrderId, cancellationToken) ?? throw new KeyNotFoundException($"Order '{request.OrderId}' was not found.");
        OrderStatus status = ParseOrderStatus(request.Status);
        DateTimeOffset occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;

        order.TransitionTo(status, request.FilledQuantity, occurredAt);
        await _orderRepository.UpdateOrderAsync(order, cancellationToken);

        ExecutionEvent executionEvent = new(order.Id, status, order.FilledQuantity, occurredAt);
        await _orderRepository.AddOrderEventAsync(executionEvent, cancellationToken);

        await _positionSnapshotRepository.UpsertPositionSnapshotAsync(
            new PositionSnapshot(
                order.TradeIntent.Ticker,
                order.TradeIntent.Side ?? throw new DomainException("Execution updates require a persisted trade-intent side."),
                order.FilledQuantity,
                order.TradeIntent.LimitPrice ?? throw new DomainException("Execution updates require a persisted limit price."),
                occurredAt),
            cancellationToken);

        OrderResponse orderResponse = await OrderResponseFactory.CreateAsync(order, _orderRepository, cancellationToken);
        return new ExecutionUpdateResult(order.Id, status.ToString().ToLowerInvariant(), order.FilledQuantity, occurredAt, orderResponse);
    }

    private static TradeSide ParseSide(string side)
    {
        if (Enum.TryParse<TradeSide>(side, ignoreCase: true, out TradeSide parsed))
        {
            return parsed;
        }

        throw new DomainException("Side must be either 'yes' or 'no'.");
    }

    private static TradeSide? ParseOptionalSide(string? side)
    {
        if (string.IsNullOrWhiteSpace(side))
        {
            return null;
        }

        return ParseSide(side);
    }

    private static TradeIntentActionType ParseActionType(string actionType)
    {
        if (Enum.TryParse<TradeIntentActionType>(actionType, ignoreCase: true, out TradeIntentActionType parsed))
        {
            return parsed;
        }

        throw new DomainException("Action type is invalid.");
    }

    private static OrderStatus ParseOrderStatus(string status)
    {
        string normalized = status.Replace("-", string.Empty).Replace("_", string.Empty).Trim();
        if (string.Equals(normalized, "executed", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Filled;
        }

        if (string.Equals(normalized, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Canceled;
        }

        if (Enum.TryParse<OrderStatus>(normalized, ignoreCase: true, out OrderStatus parsed))
        {
            return parsed;
        }

        throw new DomainException("Execution update status is invalid.");
    }

    private static Guid? ResolveOrderId(ApplicationEventEnvelope resultEvent)
    {
        if (!string.IsNullOrWhiteSpace(resultEvent.ResourceId) && Guid.TryParse(resultEvent.ResourceId, out Guid fromResourceId))
        {
            return fromResourceId;
        }

        return TryGetGuid(resultEvent.Attributes, "publisherOrderId");
    }

    private static OrderStatus? MapResultToOrderStatus(ApplicationEventEnvelope resultEvent, Order order)
    {
        if (resultEvent.Name.EndsWith(".dead_lettered", StringComparison.OrdinalIgnoreCase))
        {
            return OrderStatus.Rejected;
        }

        return resultEvent.Name switch
        {
            "order.execution_succeeded" => ParseOptionalOrderStatus(TryGetString(resultEvent.Attributes, "orderStatus"))
                ?? (order.TradeIntent.ActionType == TradeIntentActionType.Cancel ? OrderStatus.Canceled : OrderStatus.Accepted),
            "order.execution_failed" => OrderStatus.Rejected,
            "order.execution_blocked" => OrderStatus.Rejected,
            _ => null,
        };
    }

    private static OrderStatus? ParseOptionalOrderStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        return ParseOrderStatus(status);
    }

    private static string? TryGetString(IReadOnlyDictionary<string, string?> attributes, string key)
        => attributes.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int? TryGetInt(IReadOnlyDictionary<string, string?> attributes, string key)
        => attributes.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed) ? parsed : null;

    private static Guid? TryGetGuid(IReadOnlyDictionary<string, string?> attributes, string key)
        => attributes.TryGetValue(key, out string? value) && Guid.TryParse(value, out Guid parsed) ? parsed : null;

    private static int? InferFilledQuantity(ApplicationEventEnvelope resultEvent, Order order, OrderStatus? mappedStatus)
    {
        if (mappedStatus == OrderStatus.Filled && order.TradeIntent.Quantity.HasValue)
        {
            return order.TradeIntent.Quantity.Value;
        }

        return null;
    }
}
