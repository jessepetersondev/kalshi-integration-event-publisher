using Kalshi.Integration.Domain.Orders;

namespace Kalshi.Integration.Application.Trading;

/// <summary>
/// Describes the publisher projection changes derived from a single executor result event.
/// </summary>
public sealed record ResultProjectionMutation(
    Guid OrderId,
    string ResultStatus,
    OrderStatus? NextStatus,
    int FilledQuantity,
    string? Details,
    string? ExternalOrderId,
    string? ClientOrderId,
    Guid? CommandEventId,
    bool UpdatePositionSnapshot);
