using Kalshi.Integration.Domain.Positions;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Persists the latest position snapshot for each ticker and side.
/// </summary>
public interface IPositionSnapshotRepository
{
    Task UpsertPositionSnapshotAsync(PositionSnapshot positionSnapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken cancellationToken = default);
}
