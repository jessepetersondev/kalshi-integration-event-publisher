namespace Kalshi.Integration.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents the persistence model for audit record.
/// </summary>
public sealed class AuditRecordEntity
{
    public Guid Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string? ResourceId { get; set; }
    public string Details { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
}
