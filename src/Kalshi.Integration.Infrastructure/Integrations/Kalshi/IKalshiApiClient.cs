using System.Text.Json.Nodes;

namespace Kalshi.Integration.Infrastructure.Integrations.Kalshi;

/// <summary>
/// Defines direct Kalshi API operations used by the publisher bridge.
/// </summary>
public interface IKalshiApiClient
{
    /// <summary>
    /// Returns the configured Kalshi subaccount balance payload.
    /// </summary>
    Task<JsonNode> GetBalanceAsync(int subaccount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the configured Kalshi positions payload.
    /// </summary>
    Task<JsonNode> GetPositionsAsync(int subaccount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw Kalshi series payload.
    /// </summary>
    Task<JsonNode> GetSeriesAsync(string? category, IReadOnlyList<string> tags, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw Kalshi markets payload.
    /// </summary>
    Task<JsonNode> GetMarketsAsync(string? status, int limit, string? seriesTicker, string? cursor, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw Kalshi market payload for a single ticker.
    /// </summary>
    Task<JsonNode> GetMarketAsync(string ticker, CancellationToken cancellationToken = default);

    /// <summary>
    /// Places a Kalshi order using the supplied bridge payload.
    /// </summary>
    Task<JsonNode> PlaceOrderAsync(JsonObject payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the raw Kalshi order payload for the supplied external order id.
    /// </summary>
    Task<JsonNode> GetOrderAsync(string externalOrderId, int subaccount, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the raw Kalshi order payload for the supplied external order id.
    /// </summary>
    Task<JsonNode> CancelOrderAsync(string externalOrderId, int subaccount, CancellationToken cancellationToken = default);
}
