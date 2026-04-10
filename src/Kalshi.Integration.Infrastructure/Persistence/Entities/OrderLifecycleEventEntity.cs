namespace Kalshi.Integration.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents the persistence model for order lifecycle event.
/// </summary>
public sealed class OrderLifecycleEventEntity
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string? Details { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
