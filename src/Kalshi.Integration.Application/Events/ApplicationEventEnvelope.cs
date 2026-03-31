namespace Kalshi.Integration.Application.Events;

/// <summary>
/// Represents the normalized application event published to downstream consumers.
/// </summary>
public sealed record ApplicationEventEnvelope(
    Guid Id,
    string Category,
    string Name,
    string? ResourceId,
    string? CorrelationId,
    string? IdempotencyKey,
    IReadOnlyDictionary<string, string?> Attributes,
    DateTimeOffset OccurredAt)
{
    public static ApplicationEventEnvelope Create(
        string category,
        string name,
        string? resourceId = null,
        string? correlationId = null,
        string? idempotencyKey = null,
        IReadOnlyDictionary<string, string?>? attributes = null,
        DateTimeOffset? occurredAt = null)
    {
        return new ApplicationEventEnvelope(
            Guid.NewGuid(),
            category,
            name,
            resourceId,
            correlationId,
            idempotencyKey,
            attributes ?? new Dictionary<string, string?>(),
            occurredAt ?? DateTimeOffset.UtcNow);
    }
}
