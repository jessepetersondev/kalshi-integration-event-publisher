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
    RetryScheduled = 5,
    ManualInterventionRequired = 6,
#pragma warning disable CA1069
    PublishPendingReview = ManualInterventionRequired,
#pragma warning restore CA1069
}
