namespace Kalshi.Integration.Infrastructure.Persistence.Entities;

/// <summary>
/// Represents the persistence model for position snapshot.
/// </summary>
public sealed class PositionSnapshotEntity
{
    public Guid Id { get; set; }
    public string Ticker { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public int Contracts { get; set; }
    public decimal AveragePrice { get; set; }
    public DateTimeOffset AsOf { get; set; }
}
