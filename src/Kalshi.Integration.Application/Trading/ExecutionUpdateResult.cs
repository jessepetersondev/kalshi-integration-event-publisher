using Kalshi.Integration.Contracts.Orders;

namespace Kalshi.Integration.Application.Trading;

/// <summary>
/// Represents the result of execution update.
/// </summary>


public sealed record ExecutionUpdateResult(
    Guid OrderId,
    string Status,
    int FilledQuantity,
    DateTimeOffset OccurredAt,
    OrderResponse Order);
