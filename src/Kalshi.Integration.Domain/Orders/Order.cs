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
            [OrderStatus.Pending] = [OrderStatus.Accepted, OrderStatus.Resting, OrderStatus.PartiallyFilled, OrderStatus.Filled, OrderStatus.Rejected, OrderStatus.Canceled],
            [OrderStatus.Accepted] = [OrderStatus.Resting, OrderStatus.PartiallyFilled, OrderStatus.Filled, OrderStatus.Canceled, OrderStatus.Rejected],
            [OrderStatus.Resting] = [OrderStatus.PartiallyFilled, OrderStatus.Filled, OrderStatus.Canceled],
            [OrderStatus.PartiallyFilled] = [OrderStatus.PartiallyFilled, OrderStatus.Filled, OrderStatus.Canceled],
            [OrderStatus.Filled] = [OrderStatus.Settled],
            [OrderStatus.Canceled] = [],
            [OrderStatus.Rejected] = [],
            [OrderStatus.Settled] = [],
        };

    public Order(TradeIntent tradeIntent)
    {
        TradeIntent = tradeIntent ?? throw new ArgumentNullException(nameof(tradeIntent));
        Id = Guid.NewGuid();
        CurrentStatus = OrderStatus.Pending;
        PublishStatus = OrderPublishStatus.OrderCreated;
        FilledQuantity = 0;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }
    public TradeIntent TradeIntent { get; }
    public OrderStatus CurrentStatus { get; private set; }
    public OrderPublishStatus PublishStatus { get; private set; }
    public string? LastResultStatus { get; private set; }
    public string? LastResultMessage { get; private set; }
    public string? ExternalOrderId { get; private set; }
    public string? ClientOrderId { get; private set; }
    public Guid? CommandEventId { get; private set; }
    public int FilledQuantity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void SetPersistenceState(
        Guid id,
        OrderStatus currentStatus,
        int filledQuantity,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        OrderPublishStatus publishStatus = currentStatus switch
        {
            OrderStatus.Pending => OrderPublishStatus.OrderCreated,
            _ => OrderPublishStatus.PublishConfirmed,
        };

        SetPersistenceState(
            id,
            currentStatus,
            publishStatus,
            lastResultStatus: null,
            lastResultMessage: null,
            externalOrderId: null,
            clientOrderId: null,
            commandEventId: null,
            filledQuantity: filledQuantity,
            createdAt: createdAt,
            updatedAt: updatedAt);
    }

    public void SetPersistenceState(
        Guid id,
        OrderStatus currentStatus,
        OrderPublishStatus publishStatus,
        string? lastResultStatus,
        string? lastResultMessage,
        string? externalOrderId,
        string? clientOrderId,
        Guid? commandEventId,
        int filledQuantity,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        Id = id;
        CurrentStatus = currentStatus;
        PublishStatus = publishStatus;
        LastResultStatus = lastResultStatus;
        LastResultMessage = lastResultMessage;
        ExternalOrderId = externalOrderId;
        ClientOrderId = clientOrderId;
        CommandEventId = commandEventId;
        FilledQuantity = filledQuantity;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public void TransitionTo(OrderStatus nextStatus, int? filledQuantity = null, DateTimeOffset? updatedAt = null)
    {
        if (!AllowedTransitions.TryGetValue(CurrentStatus, out OrderStatus[]? allowed) || !allowed.Contains(nextStatus))
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

            if (TradeIntent.Quantity.HasValue && filledQuantity.Value > TradeIntent.Quantity.Value)
            {
                throw new DomainException("Filled quantity cannot exceed order quantity.");
            }

            FilledQuantity = filledQuantity.Value;
        }

        if (nextStatus == OrderStatus.Filled && TradeIntent.Quantity.HasValue && FilledQuantity != TradeIntent.Quantity.Value)
        {
            throw new DomainException("Filled orders must have a filled quantity equal to the full order quantity.");
        }

        if (nextStatus == OrderStatus.PartiallyFilled && (!TradeIntent.Quantity.HasValue || FilledQuantity <= 0 || FilledQuantity >= TradeIntent.Quantity.Value))
        {
            throw new DomainException("Partially filled orders must have a partial fill quantity.");
        }

        CurrentStatus = nextStatus;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkPublishAttempted(DateTimeOffset? updatedAt = null)
    {
        PublishStatus = OrderPublishStatus.PublishAttempted;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkCommandQueued(Guid commandEventId, string? clientOrderId, DateTimeOffset? updatedAt = null)
    {
        PublishStatus = OrderPublishStatus.OrderCreated;
        CommandEventId = commandEventId;
        ClientOrderId = string.IsNullOrWhiteSpace(clientOrderId) ? ClientOrderId : clientOrderId.Trim();
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkPublishConfirmed(Guid commandEventId, DateTimeOffset? updatedAt = null)
    {
        PublishStatus = OrderPublishStatus.PublishConfirmed;
        CommandEventId = commandEventId;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkRetryScheduled(string? reason, Guid? commandEventId = null, DateTimeOffset? updatedAt = null)
    {
        PublishStatus = OrderPublishStatus.RetryScheduled;
        LastResultMessage = string.IsNullOrWhiteSpace(reason) ? LastResultMessage : reason.Trim();
        if (!CommandEventId.HasValue && commandEventId.HasValue)
        {
            CommandEventId = commandEventId.Value;
        }

        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkManualInterventionRequired(string? reason, Guid? commandEventId = null, DateTimeOffset? updatedAt = null)
    {
        PublishStatus = OrderPublishStatus.ManualInterventionRequired;
        LastResultMessage = string.IsNullOrWhiteSpace(reason) ? LastResultMessage : reason.Trim();
        if (!CommandEventId.HasValue && commandEventId.HasValue)
        {
            CommandEventId = commandEventId.Value;
        }

        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public void MarkPublishPendingReview(string? reason, Guid? commandEventId = null, DateTimeOffset? updatedAt = null)
        => MarkManualInterventionRequired(reason, commandEventId, updatedAt);

    public void ApplyResult(
        string resultStatus,
        OrderStatus? nextStatus = null,
        int? filledQuantity = null,
        string? lastResultMessage = null,
        string? externalOrderId = null,
        string? clientOrderId = null,
        Guid? commandEventId = null,
        DateTimeOffset? updatedAt = null)
    {
        if (string.IsNullOrWhiteSpace(resultStatus))
        {
            throw new DomainException("Result status is required.");
        }

        if (nextStatus.HasValue && nextStatus.Value != CurrentStatus)
        {
            TransitionTo(nextStatus.Value, filledQuantity, updatedAt);
        }
        else if (filledQuantity.HasValue)
        {
            if (filledQuantity.Value < 0)
            {
                throw new DomainException("Filled quantity cannot be negative.");
            }

            if (TradeIntent.Quantity.HasValue && filledQuantity.Value > TradeIntent.Quantity.Value)
            {
                throw new DomainException("Filled quantity cannot exceed order quantity.");
            }

            FilledQuantity = filledQuantity.Value;
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
        }
        else
        {
            UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
        }

        LastResultStatus = resultStatus.Trim();
        LastResultMessage = string.IsNullOrWhiteSpace(lastResultMessage) ? LastResultMessage : lastResultMessage.Trim();
        ExternalOrderId = string.IsNullOrWhiteSpace(externalOrderId) ? ExternalOrderId : externalOrderId.Trim();
        ClientOrderId = string.IsNullOrWhiteSpace(clientOrderId) ? ClientOrderId : clientOrderId.Trim();
        if (!CommandEventId.HasValue && commandEventId.HasValue)
        {
            CommandEventId = commandEventId.Value;
        }
    }
}
