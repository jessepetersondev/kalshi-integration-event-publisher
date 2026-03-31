using Kalshi.Integration.Contracts.Orders;

namespace Kalshi.Integration.Application.Trading;

/// <summary>
/// Represents the persisted order state after an execution update is applied.
/// </summary>
public sealed record ExecutionUpdateResult(
    Guid OrderId,
    string Status,
    int FilledQuantity,
    DateTimeOffset OccurredAt,
    OrderResponse Order);
