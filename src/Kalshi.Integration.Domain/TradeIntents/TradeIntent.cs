using Kalshi.Integration.Domain.Common;

namespace Kalshi.Integration.Domain.TradeIntents;

/// <summary>
/// Represents the domain model for trade intent.
/// </summary>
public sealed class TradeIntent
{
    public TradeIntent(
        string ticker,
        TradeSide side,
        int quantity,
        decimal limitPrice,
        string strategyName,
        string? correlationId = null,
        DateTimeOffset? createdAt = null)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            throw new DomainException("Ticker is required.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }

        if (limitPrice <= 0m || limitPrice > 1m)
        {
            throw new DomainException("Limit price must be greater than 0 and less than or equal to 1.");
        }

        if (string.IsNullOrWhiteSpace(strategyName))
        {
            throw new DomainException("Strategy name is required.");
        }

        Id = Guid.NewGuid();
        Ticker = ticker.Trim().ToUpperInvariant();
        Side = side;
        Quantity = quantity;
        LimitPrice = decimal.Round(limitPrice, 4, MidpointRounding.AwayFromZero);
        StrategyName = strategyName.Trim();
        CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Id.ToString("N") : correlationId.Trim();
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Ticker { get; }
    public TradeSide Side { get; }
    public int Quantity { get; }
    public decimal LimitPrice { get; }
    public string StrategyName { get; }
    public string CorrelationId { get; }
    public DateTimeOffset CreatedAt { get; }

    public TradeIntent WithId(Guid id)
    {
        Id = id;
        return this;
    }
}
