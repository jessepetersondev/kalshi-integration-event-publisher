using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Persists orders together with their execution-event history.
/// </summary>
public interface IOrderRepository
{
    Task AddOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task UpdateOrderAsync(Order order, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default);
    Task AddOrderEventAsync(ExecutionEvent executionEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExecutionEvent>> GetOrderEventsAsync(Guid orderId, CancellationToken cancellationToken = default);
}
