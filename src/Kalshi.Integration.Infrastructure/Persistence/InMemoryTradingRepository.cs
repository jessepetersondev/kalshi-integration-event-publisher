using System.Collections.Concurrent;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;

namespace Kalshi.Integration.Infrastructure.Persistence;

/// <summary>
/// Provides an in-memory trading repository for local development and tests.
/// </summary>
public sealed class InMemoryTradingRepository : ITradeIntentRepository, IOrderRepository, IPositionSnapshotRepository
{
    private readonly ConcurrentDictionary<Guid, TradeIntent> _tradeIntents = new();
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<ExecutionEvent>> _orderEvents = new();
    private readonly ConcurrentDictionary<string, PositionSnapshot> _positions = new();

    public Task AddTradeIntentAsync(TradeIntent tradeIntent, CancellationToken cancellationToken = default)
    {
        _tradeIntents[tradeIntent.Id] = tradeIntent;
        return Task.CompletedTask;
    }

    public Task<TradeIntent?> GetTradeIntentAsync(Guid tradeIntentId, CancellationToken cancellationToken = default)
    {
        _tradeIntents.TryGetValue(tradeIntentId, out var tradeIntent);
        return Task.FromResult(tradeIntent);
    }

    public Task<TradeIntent?> GetTradeIntentByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        var tradeIntent = _tradeIntents.Values.FirstOrDefault(x => string.Equals(x.CorrelationId, correlationId, StringComparison.Ordinal));
        return Task.FromResult(tradeIntent);
    }

    public Task AddOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task UpdateOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task<Order?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(orderId, out var order);
        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<Order>>(_orders.Values.OrderByDescending(x => x.UpdatedAt).ToArray());
    }

    public Task AddOrderEventAsync(ExecutionEvent executionEvent, CancellationToken cancellationToken = default)
    {
        var queue = _orderEvents.GetOrAdd(executionEvent.OrderId, _ => new ConcurrentQueue<ExecutionEvent>());
        queue.Enqueue(executionEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutionEvent>> GetOrderEventsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (_orderEvents.TryGetValue(orderId, out var queue))
        {
            return Task.FromResult<IReadOnlyList<ExecutionEvent>>(queue.ToArray());
        }

        return Task.FromResult<IReadOnlyList<ExecutionEvent>>(Array.Empty<ExecutionEvent>());
    }

    public Task UpsertPositionSnapshotAsync(PositionSnapshot positionSnapshot, CancellationToken cancellationToken = default)
    {
        var key = $"{positionSnapshot.Ticker}:{positionSnapshot.Side}";
        _positions[key] = positionSnapshot;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PositionSnapshot>>(_positions.Values.OrderBy(p => p.Ticker).ToArray());
    }
}
