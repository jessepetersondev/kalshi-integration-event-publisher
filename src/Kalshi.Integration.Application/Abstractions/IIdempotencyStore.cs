using Kalshi.Integration.Application.Operations;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Provides storage operations for i idempotency.
/// </summary>


public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> GetAsync(string scope, string key, CancellationToken cancellationToken = default);
    Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default);
}
