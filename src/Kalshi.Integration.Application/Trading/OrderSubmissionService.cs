using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;

namespace Kalshi.Integration.Application.Trading;

/// <summary>
/// Creates publisher-owned orders and queues their outbound command messages
/// in the same durable transaction.
/// </summary>
public sealed class OrderSubmissionService
{
    private readonly ITradeIntentRepository _tradeIntentRepository;
    private readonly IOrderCommandSubmissionStore _submissionStore;
    private readonly IOrderRepository _orderRepository;

    public OrderSubmissionService(
        ITradeIntentRepository tradeIntentRepository,
        IOrderCommandSubmissionStore submissionStore,
        IOrderRepository orderRepository)
    {
        _tradeIntentRepository = tradeIntentRepository;
        _submissionStore = submissionStore;
        _orderRepository = orderRepository;
    }

    public async Task<OrderResponse> SubmitOrderAsync(
        CreateOrderRequest request,
        string correlationId,
        string? idempotencyKey,
        IReadOnlyDictionary<string, string?>? additionalAttributes = null,
        CancellationToken cancellationToken = default)
    {
        var tradeIntent = await _tradeIntentRepository.GetTradeIntentAsync(request.TradeIntentId, cancellationToken);
        if (tradeIntent is null)
        {
            throw new KeyNotFoundException($"Trade intent '{request.TradeIntentId}' was not found.");
        }

        var order = new Order(tradeIntent);
        var clientOrderId = WeatherQuantCommandMapper.ResolveClientOrderId(order);
        var commandEvent = WeatherQuantCommandMapper.CreateOrderEvent(order, correlationId, idempotencyKey, clientOrderId, additionalAttributes);
        order.MarkCommandQueued(commandEvent.Id, clientOrderId, commandEvent.OccurredAt);

        PositionSnapshot? initialPositionSnapshot = null;
        if (tradeIntent.Side.HasValue && tradeIntent.LimitPrice.HasValue)
        {
            initialPositionSnapshot = new PositionSnapshot(
                tradeIntent.Ticker,
                tradeIntent.Side.Value,
                0,
                tradeIntent.LimitPrice.Value,
                order.UpdatedAt);
        }

        await _submissionStore.SubmitOrderWithCommandAsync(order, commandEvent, initialPositionSnapshot, cancellationToken);
        return await OrderResponseFactory.CreateAsync(order, _orderRepository, cancellationToken);
    }
}
