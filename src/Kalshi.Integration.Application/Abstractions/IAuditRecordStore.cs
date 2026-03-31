using Kalshi.Integration.Application.Operations;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Persists audit records that describe accepted, rejected, and replayed operations.
/// </summary>
public interface IAuditRecordStore
{
    Task AddAsync(AuditRecord auditRecord, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditRecord>> GetRecentAsync(string? category = null, int hours = 24, int limit = 100, CancellationToken cancellationToken = default);
}
