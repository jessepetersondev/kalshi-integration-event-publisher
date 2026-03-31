using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.TradeIntents;

namespace Kalshi.Integration.Domain.Orders;

/// <summary>
/// Represents the domain model for order.
/// </summary>
public sealed class Order
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions =
        new()
        {
            [OrderStatus.Pending] = new[] { OrderStatus.Accepted, OrderStatus.Rejected, OrderStatus.Canceled },
            [OrderStatus.Accepted] = new[] { OrderStatus.Resting, OrderStatus.PartiallyFilled, OrderStatus.Filled, OrderStatus.Canceled, OrderStatus.Rejected },
            [OrderStatus.Resting] = new[] { OrderStatus.PartiallyFilled, OrderStatus.Filled, OrderStatus.Canceled },
            [OrderStatus.PartiallyFilled] = new[] { OrderStatus.PartiallyFilled, OrderStatus.Filled, OrderStatus.Canceled },
            [OrderStatus.Filled] = new[] { OrderStatus.Settled },
            [OrderStatus.Canceled] = Array.Empty<OrderStatus>(),
            [OrderStatus.Rejected] = Array.Empty<OrderStatus>(),
            [OrderStatus.Settled] = Array.Empty<OrderStatus>(),
        };

    public Order(TradeIntent tradeIntent)
    {
        TradeIntent = tradeIntent ?? throw new ArgumentNullException(nameof(tradeIntent));
        Id = Guid.NewGuid();
        CurrentStatus = OrderStatus.Pending;
        FilledQuantity = 0;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public TradeIntent TradeIntent { get; }
    public OrderStatus CurrentStatus { get; private set; }
    public int FilledQuantity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetPersistenceState(Guid id, OrderStatus currentStatus, int filledQuantity, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        Id = id;
        CurrentStatus = currentStatus;
        FilledQuantity = filledQuantity;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public void TransitionTo(OrderStatus nextStatus, int? filledQuantity = null, DateTimeOffset? updatedAt = null)
    {
        if (!AllowedTransitions.TryGetValue(CurrentStatus, out var allowed) || !allowed.Contains(nextStatus))
        {
            throw new DomainException($"Invalid order status transition: {CurrentStatus} -> {nextStatus}.");
        }

        if (filledQuantity.HasValue)
        {
            if (filledQuantity.Value < 0)
            {
                throw new DomainException("Filled quantity cannot be negative.");
            }

            if (filledQuantity.Value < FilledQuantity)
            {
                throw new DomainException("Filled quantity cannot move backwards.");
            }

            if (filledQuantity.Value > TradeIntent.Quantity)
            {
                throw new DomainException("Filled quantity cannot exceed order quantity.");
            }

            FilledQuantity = filledQuantity.Value;
        }

        if (nextStatus == OrderStatus.Filled && FilledQuantity != TradeIntent.Quantity)
        {
            throw new DomainException("Filled orders must have a filled quantity equal to the full order quantity.");
        }

        if (nextStatus == OrderStatus.PartiallyFilled && (FilledQuantity <= 0 || FilledQuantity >= TradeIntent.Quantity))
        {
            throw new DomainException("Partially filled orders must have a partial fill quantity.");
        }

        CurrentStatus = nextStatus;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }
}
