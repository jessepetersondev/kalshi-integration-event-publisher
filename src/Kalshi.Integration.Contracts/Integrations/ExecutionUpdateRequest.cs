namespace Kalshi.Integration.Contracts.Integrations;

/// <summary>
/// Represents a request payload for execution update.
/// </summary>
public sealed record ExecutionUpdateRequest(
    Guid OrderId,
    string Status,
    int FilledQuantity,
    DateTimeOffset? OccurredAt,
    string? CorrelationId);
