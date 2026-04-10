namespace Kalshi.Integration.Domain.Orders;

/// <summary>
/// Defines the supported order publish lifecycle states.
/// </summary>
public enum OrderPublishStatus
{
    Accepted = 1,
    OrderCreated = 2,
    PublishAttempted = 3,
    PublishConfirmed = 4,
    PublishPendingReview = 5,
}
