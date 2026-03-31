using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.TradeIntents;

namespace Kalshi.Integration.Domain.Positions;

/// <summary>
/// Represents a snapshot of position state.
/// </summary>


public sealed class PositionSnapshot
{
    public PositionSnapshot(string ticker, TradeSide side, int contracts, decimal averagePrice, DateTimeOffset asOf)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new DomainException("Ticker is required.");
        }

        if (contracts < 0)
        {
            throw new DomainException("Contracts cannot be negative.");
        }

        if (averagePrice < 0m || averagePrice > 1m)
        {
            throw new DomainException("Average price must be between 0 and 1.");
        }

        Ticker = ticker.Trim().ToUpperInvariant();
        Side = side;
        Contracts = contracts;
        AveragePrice = decimal.Round(averagePrice, 4, MidpointRounding.AwayFromZero);
        AsOf = asOf;
    }

    public string Ticker { get; }
    public TradeSide Side { get; }
    public int Contracts { get; }
    public decimal AveragePrice { get; }
    public DateTimeOffset AsOf { get; }
}
