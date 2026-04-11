using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Persists publisher-owned order state together with the outbound order-command message
/// in the same durable transaction.
/// </summary>
public interface IOrderCommandSubmissionStore
{
    Task SubmitOrderWithCommandAsync(
        Order order,
        ApplicationEventEnvelope commandEvent,
        PositionSnapshot? initialPositionSnapshot,
        CancellationToken cancellationToken = default);
}
