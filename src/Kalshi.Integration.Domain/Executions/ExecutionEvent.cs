using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.Orders;

namespace Kalshi.Integration.Domain.Executions;

/// <summary>
/// Represents the domain model for execution event.
/// </summary>


public sealed class ExecutionEvent
{
    public ExecutionEvent(Guid orderId, OrderStatus status, int filledQuantity, DateTimeOffset occurredAt)
    {
        if (orderId == Guid.Empty)
        {
            throw new DomainException("Order id is required.");
        }

        if (filledQuantity < 0)
        {
            throw new DomainException("Filled quantity cannot be negative.");
        }

        Id = Guid.NewGuid();
        OrderId = orderId;
        Status = status;
        FilledQuantity = filledQuantity;
        OccurredAt = occurredAt;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; }
    public OrderStatus Status { get; }
    public int FilledQuantity { get; }
    public DateTimeOffset OccurredAt { get; }

    public ExecutionEvent WithId(Guid id)
    {
        Id = id;
        return this;
    }
}
