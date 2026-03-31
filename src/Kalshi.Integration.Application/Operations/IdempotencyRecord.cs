namespace Kalshi.Integration.Application.Operations;

/// <summary>
/// Represents the stored response and request fingerprint for an idempotent operation.
/// </summary>
public sealed record IdempotencyRecord(
    Guid Id,
    string Scope,
    string Key,
    string RequestHash,
    int StatusCode,
    string ResponseBody,
    DateTimeOffset CreatedAt)
{
    public static IdempotencyRecord Create(
        string scope,
        string key,
        string requestHash,
        int statusCode,
        string responseBody,
        DateTimeOffset? createdAt = null)
    {
        return new IdempotencyRecord(
            Guid.NewGuid(),
            scope,
            key,
            requestHash,
            statusCode,
            responseBody,
            createdAt ?? DateTimeOffset.UtcNow);
    }
}
