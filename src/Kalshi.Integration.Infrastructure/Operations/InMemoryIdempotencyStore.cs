using System.Collections.Concurrent;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Operations;

namespace Kalshi.Integration.Infrastructure.Operations;

/// <summary>
/// Provides storage operations for in memory idempotency.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, IdempotencyRecord> _records = new(StringComparer.Ordinal);

    public Task<IdempotencyRecord?> GetAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        _records.TryGetValue(BuildKey(scope, key), out var record);
        return Task.FromResult(record);
    }

    public Task SaveAsync(IdempotencyRecord record, CancellationToken cancellationToken = default)
    {
        _records[BuildKey(record.Scope, record.Key)] = record;
        return Task.CompletedTask;
    }

    private static string BuildKey(string scope, string key) => $"{scope}:{key}";
}
