namespace Kalshi.Integration.Contracts.Positions;

/// <summary>
/// Represents a response payload for position.
/// </summary>


public sealed record PositionResponse(
    string Ticker,
    string Side,
    int Contracts,
    decimal AveragePrice,
    DateTimeOffset AsOf);
