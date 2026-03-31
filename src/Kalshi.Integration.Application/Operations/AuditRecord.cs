namespace Kalshi.Integration.Application.Operations;

/// <summary>
/// Represents a durable audit record for an API or integration action.
/// </summary>
public sealed record AuditRecord(
    Guid Id,
    string Category,
    string Action,
    string Outcome,
    string CorrelationId,
    string? IdempotencyKey,
    string? ResourceId,
    string Details,
    DateTimeOffset OccurredAt)
{
    public static AuditRecord Create(
        string category,
        string action,
        string outcome,
        string correlationId,
        string details,
        string? idempotencyKey = null,
        string? resourceId = null,
        DateTimeOffset? occurredAt = null)
    {
        return new AuditRecord(
            Guid.NewGuid(),
            category,
            action,
            outcome,
            correlationId,
            idempotencyKey,
            resourceId,
            details,
            occurredAt ?? DateTimeOffset.UtcNow);
    }
}
