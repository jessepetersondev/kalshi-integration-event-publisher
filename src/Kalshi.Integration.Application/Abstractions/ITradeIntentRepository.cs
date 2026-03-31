using Kalshi.Integration.Domain.TradeIntents;

namespace Kalshi.Integration.Application.Abstractions;

/// <summary>
/// Provides persistence operations for i trade intent.
/// </summary>


public interface ITradeIntentRepository
{
    Task AddTradeIntentAsync(TradeIntent tradeIntent, CancellationToken cancellationToken = default);
    Task<TradeIntent?> GetTradeIntentAsync(Guid tradeIntentId, CancellationToken cancellationToken = default);
    Task<TradeIntent?> GetTradeIntentByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default);
}
