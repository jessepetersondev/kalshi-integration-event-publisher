namespace Kalshi.Integration.Contracts.Orders;

/// <summary>
/// Represents a response payload for order lifecycle event.
/// </summary>
public sealed record OrderLifecycleEventResponse(
    string Stage,
    string? Details,
    DateTimeOffset OccurredAt);
