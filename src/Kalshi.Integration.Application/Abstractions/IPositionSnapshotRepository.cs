using Kalshi.Integration.Domain.Positions;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Provides persistence operations for i position snapshot.
/// </summary>


public interface IPositionSnapshotRepository
{
    Task UpsertPositionSnapshotAsync(PositionSnapshot positionSnapshot, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken cancellationToken = default);
}
