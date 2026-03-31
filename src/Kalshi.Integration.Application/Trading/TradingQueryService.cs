using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Contracts.Positions;

namespace Kalshi.Integration.Application.Trading;

/// <summary>
/// Reads order and position projections without mutating trading state.
/// </summary>
public sealed class TradingQueryService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPositionSnapshotRepository _positionSnapshotRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="TradingQueryService"/> class.
    /// </summary>
    /// <param name="orderRepository">The repository used to read orders.</param>
    /// <param name="positionSnapshotRepository">The repository used to read position snapshots.</param>
    public TradingQueryService(IOrderRepository orderRepository, IPositionSnapshotRepository positionSnapshotRepository)
    {
        _orderRepository = orderRepository;
        _positionSnapshotRepository = positionSnapshotRepository;
    }

    public async Task<OrderResponse?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        return await OrderResponseFactory.CreateAsync(order, _orderRepository, cancellationToken);
    }

    public async Task<IReadOnlyList<PositionResponse>> GetPositionsAsync(CancellationToken cancellationToken = default)
    {
        var positions = await _positionSnapshotRepository.GetPositionsAsync(cancellationToken);
        return positions
            .OrderBy(position => position.Ticker)
            .Select(position => new PositionResponse(
                position.Ticker,
                position.Side.ToString().ToLowerInvariant(),
                position.Contracts,
                position.AveragePrice,
                position.AsOf))
            .ToArray();
    }
}
