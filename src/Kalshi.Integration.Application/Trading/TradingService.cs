using Kalshi.Integration.Application.Abstractions;
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
public sealed class TradingService
{
    private readonly ITradeIntentRepository _tradeIntentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IPositionSnapshotRepository _positionSnapshotRepository;
    private readonly RiskEvaluator _riskEvaluator;

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
    {
        _tradeIntentRepository = tradeIntentRepository;
        _orderRepository = orderRepository;
        _positionSnapshotRepository = positionSnapshotRepository;
        _riskEvaluator = riskEvaluator;
    }

    public async Task<TradeIntentResponse> CreateTradeIntentAsync(CreateTradeIntentRequest request, CancellationToken cancellationToken = default)
    {
        var riskDecision = await _riskEvaluator.EvaluateTradeIntentAsync(request, cancellationToken);
        if (!riskDecision.Accepted)
        {
            throw new DomainException(string.Join(" ", riskDecision.Reasons));
        }

        var tradeIntent = new TradeIntent(
            request.Ticker,
            ParseSide(request.Side),
            request.Quantity,
            request.LimitPrice,
            request.StrategyName,
            request.CorrelationId);

        await _tradeIntentRepository.AddTradeIntentAsync(tradeIntent, cancellationToken);

        return new TradeIntentResponse(
            tradeIntent.Id,
            tradeIntent.Ticker,
            tradeIntent.Side.ToString().ToLowerInvariant(),
            tradeIntent.Quantity,
            tradeIntent.LimitPrice,
            tradeIntent.StrategyName,
            tradeIntent.CorrelationId,
            tradeIntent.CreatedAt,
            new RiskDecisionResponse(
                riskDecision.Accepted,
                riskDecision.Decision,
                riskDecision.Reasons.ToArray(),
                riskDecision.MaxOrderSize,
                riskDecision.DuplicateCorrelationIdDetected));
    }

    public async Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var tradeIntent = await _tradeIntentRepository.GetTradeIntentAsync(request.TradeIntentId, cancellationToken);
        if (tradeIntent is null)
        {
            throw new KeyNotFoundException($"Trade intent '{request.TradeIntentId}' was not found.");
        }

        var order = new Order(tradeIntent);
        await _orderRepository.AddOrderAsync(order, cancellationToken);
        await _orderRepository.AddOrderEventAsync(new ExecutionEvent(order.Id, order.CurrentStatus, order.FilledQuantity, order.CreatedAt), cancellationToken);
        await _positionSnapshotRepository.UpsertPositionSnapshotAsync(new PositionSnapshot(tradeIntent.Ticker, tradeIntent.Side, 0, tradeIntent.LimitPrice, order.UpdatedAt), cancellationToken);

        return await OrderResponseFactory.CreateAsync(order, _orderRepository, cancellationToken);
    }

    public async Task<ExecutionUpdateResult> ApplyExecutionUpdateAsync(ExecutionUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            throw new KeyNotFoundException($"Order '{request.OrderId}' was not found.");
        }

        var status = ParseOrderStatus(request.Status);
        var occurredAt = request.OccurredAt ?? DateTimeOffset.UtcNow;

        order.TransitionTo(status, request.FilledQuantity, occurredAt);
        await _orderRepository.UpdateOrderAsync(order, cancellationToken);

        var executionEvent = new ExecutionEvent(order.Id, status, order.FilledQuantity, occurredAt);
        await _orderRepository.AddOrderEventAsync(executionEvent, cancellationToken);

        await _positionSnapshotRepository.UpsertPositionSnapshotAsync(
            new PositionSnapshot(
                order.TradeIntent.Ticker,
                order.TradeIntent.Side,
                order.FilledQuantity,
                order.TradeIntent.LimitPrice,
                occurredAt),
            cancellationToken);

        var orderResponse = await OrderResponseFactory.CreateAsync(order, _orderRepository, cancellationToken);
        return new ExecutionUpdateResult(order.Id, status.ToString().ToLowerInvariant(), order.FilledQuantity, occurredAt, orderResponse);
    }

    private static TradeSide ParseSide(string side)
    {
        if (Enum.TryParse<TradeSide>(side, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new DomainException("Side must be either 'yes' or 'no'.");
    }

    private static OrderStatus ParseOrderStatus(string status)
    {
        if (Enum.TryParse<OrderStatus>(status.Replace("-", string.Empty).Replace("_", string.Empty), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new DomainException("Execution update status is invalid.");
    }
}
