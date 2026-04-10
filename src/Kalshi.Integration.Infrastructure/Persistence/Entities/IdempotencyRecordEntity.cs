namespace Kalshi.Integration.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents the persistence model for idempotency record.
/// </summary>
public sealed class IdempotencyRecordEntity
{
    public Guid Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
