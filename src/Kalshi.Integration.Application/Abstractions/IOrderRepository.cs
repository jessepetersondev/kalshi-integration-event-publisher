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
    Task AddOrderLifecycleEventAsync(Guid orderId, string stage, string? details, DateTimeOffset occurredAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)>> GetOrderLifecycleEventsAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<bool> TryAddResultEventAsync(Guid resultEventId, Guid? orderId, string name, string? correlationId, string? idempotencyKey, string payloadJson, DateTimeOffset occurredAt, CancellationToken cancellationToken = default);
}
