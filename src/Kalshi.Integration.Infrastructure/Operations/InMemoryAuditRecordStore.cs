using System.Collections.Concurrent;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Operations;

namespace Kalshi.Integration.Infrastructure.Operations;

/// <summary>
/// Provides storage operations for in memory audit record.
/// </summary>


public sealed class InMemoryAuditRecordStore : IAuditRecordStore
{
    private readonly ConcurrentQueue<AuditRecord> _records = new();

    public Task AddAsync(AuditRecord auditRecord, CancellationToken cancellationToken = default)
    {
        _records.Enqueue(auditRecord);

        while (_records.Count > 1000 && _records.TryDequeue(out _))
        {
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditRecord>> GetRecentAsync(string? category = null, int hours = 24, int limit = 100, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-Math.Abs(hours));
        var records = _records
            .Where(record => record.OccurredAt >= cutoff)
            .Where(record => string.IsNullOrWhiteSpace(category) || string.Equals(record.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(record => record.OccurredAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToArray();

        return Task.FromResult<IReadOnlyList<AuditRecord>>(records);
    }
}
