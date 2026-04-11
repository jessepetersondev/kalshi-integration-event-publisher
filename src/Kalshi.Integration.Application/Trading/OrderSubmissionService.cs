using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;

namespace Kalshi.Integration.Application.Trading;

/// <summary>
/// Creates publisher-owned orders and queues their outbound command messages
/// in the same durable transaction.
/// </summary>
public sealed class OrderSubmissionService(
    ITradeIntentRepository tradeIntentRepository,
    IOrderCommandSubmissionStore submissionStore,
    IOrderRepository orderRepository)
{
    private readonly ITradeIntentRepository _tradeIntentRepository = tradeIntentRepository;
    private readonly IOrderCommandSubmissionStore _submissionStore = submissionStore;
    private readonly IOrderRepository _orderRepository = orderRepository;

    public async Task<OrderResponse> SubmitOrderAsync(
        CreateOrderRequest request,
        string correlationId,
        string? idempotencyKey,
        IReadOnlyDictionary<string, string?>? additionalAttributes = null,
        CancellationToken cancellationToken = default)
    {
        Domain.TradeIntents.TradeIntent? tradeIntent = await _tradeIntentRepository.GetTradeIntentAsync(request.TradeIntentId, cancellationToken) ?? throw new KeyNotFoundException($"Trade intent '{request.TradeIntentId}' was not found.");
        Order order = new(tradeIntent);
        string clientOrderId = WeatherQuantCommandMapper.ResolveClientOrderId(order);
        Events.ApplicationEventEnvelope commandEvent = WeatherQuantCommandMapper.CreateOrderEvent(order, correlationId, idempotencyKey, clientOrderId, additionalAttributes);
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
