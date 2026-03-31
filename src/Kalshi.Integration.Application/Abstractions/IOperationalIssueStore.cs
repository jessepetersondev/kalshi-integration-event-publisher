using Kalshi.Integration.Application.Operations;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Provides storage operations for i operational issue.
/// </summary>


public interface IOperationalIssueStore
{
    Task AddAsync(OperationalIssue issue, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationalIssue>> GetRecentAsync(string? category = null, int hours = 24, CancellationToken cancellationToken = default);
}
