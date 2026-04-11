using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Infrastructure.Integrations.Kalshi;

/// <summary>
/// Executes direct HTTP requests against the Kalshi trade API.
/// </summary>
public sealed class KalshiApiClient : IKalshiApiClient
{
    private readonly HttpClient _httpClient;
    private readonly KalshiApiOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="KalshiApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The configured HTTP client.</param>
    /// <param name="options">The Kalshi API options.</param>
    public KalshiApiClient(HttpClient httpClient, IOptions<KalshiApiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<JsonNode> GetBalanceAsync(int subaccount, CancellationToken cancellationToken = default)
        => RequestAsync(
            HttpMethod.Get,
            "/portfolio/balance",
            requireAuthentication: true,
            query: [CreateQueryPair("subaccount", subaccount.ToString(CultureInfo.InvariantCulture))],
            payload: null,
            cancellationToken);

    /// <inheritdoc />
    public Task<JsonNode> GetPositionsAsync(int subaccount, CancellationToken cancellationToken = default)
        => RequestAsync(
            HttpMethod.Get,
            "/portfolio/positions",
            requireAuthentication: true,
            query:
            [
                CreateQueryPair("subaccount", subaccount.ToString(CultureInfo.InvariantCulture)),
                CreateQueryPair("count_filter", "position,total_traded"),
                CreateQueryPair("limit", "200"),
            ],
            payload: null,
            cancellationToken);

    /// <inheritdoc />
    public Task<JsonNode> GetSeriesAsync(string? category, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
    {
        var query = new List<KeyValuePair<string, string?>>();
        if (!string.IsNullOrWhiteSpace(category))
        {
            query.Add(CreateQueryPair("category", category.Trim()));
        }

        foreach (var tag in tags.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            query.Add(CreateQueryPair("tags", tag.Trim()));
        }

        return RequestAsync(HttpMethod.Get, "/series", requireAuthentication: false, query, payload: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<JsonNode> GetMarketsAsync(string? status, int limit, string? seriesTicker, string? cursor, CancellationToken cancellationToken = default)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            CreateQueryPair("limit", Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add(CreateQueryPair("status", status.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(seriesTicker))
        {
            query.Add(CreateQueryPair("series_ticker", seriesTicker.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            query.Add(CreateQueryPair("cursor", cursor.Trim()));
        }

        return RequestAsync(HttpMethod.Get, "/markets", requireAuthentication: false, query, payload: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<JsonNode> GetMarketAsync(string ticker, CancellationToken cancellationToken = default)
        => RequestAsync(HttpMethod.Get, $"/markets/{ticker.Trim()}", requireAuthentication: false, query: null, payload: null, cancellationToken);

    /// <inheritdoc />
    public Task<JsonNode> PlaceOrderAsync(JsonObject payload, CancellationToken cancellationToken = default)
        => RequestAsync(HttpMethod.Post, "/portfolio/orders", requireAuthentication: true, query: null, payload, cancellationToken);

    /// <inheritdoc />
    public Task<JsonNode> GetOrderAsync(string externalOrderId, int subaccount, CancellationToken cancellationToken = default)
        => RequestAsync(
            HttpMethod.Get,
            $"/portfolio/orders/{externalOrderId.Trim()}",
            requireAuthentication: true,
            query: [CreateQueryPair("subaccount", subaccount.ToString(CultureInfo.InvariantCulture))],
            payload: null,
            cancellationToken);

    /// <inheritdoc />
    public Task<JsonNode> GetOrdersAsync(string? ticker, int subaccount, CancellationToken cancellationToken = default)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            CreateQueryPair("subaccount", subaccount.ToString(CultureInfo.InvariantCulture)),
        };

        if (!string.IsNullOrWhiteSpace(ticker))
        {
            query.Add(CreateQueryPair("ticker", ticker.Trim()));
        }

        return RequestAsync(
            HttpMethod.Get,
            "/portfolio/orders",
            requireAuthentication: true,
            query,
            payload: null,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<JsonNode> CancelOrderAsync(string externalOrderId, int subaccount, CancellationToken cancellationToken = default)
        => RequestAsync(
            HttpMethod.Delete,
            $"/portfolio/orders/{externalOrderId.Trim()}",
            requireAuthentication: true,
            query: [CreateQueryPair("subaccount", subaccount.ToString(CultureInfo.InvariantCulture))],
            payload: null,
            cancellationToken);

    private async Task<JsonNode> RequestAsync(
        HttpMethod method,
        string path,
        bool requireAuthentication,
        IEnumerable<KeyValuePair<string, string?>>? query,
        JsonObject? payload,
        CancellationToken cancellationToken)
    {
        var normalizedPath = NormalizePath(path);
        var relativeRequestPath = normalizedPath.TrimStart('/');
        var requestUri = query is null ? relativeRequestPath : QueryHelpers.AddQueryString(relativeRequestPath, query);

        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation("User-Agent", _options.UserAgent);

        if (payload is not null)
        {
            request.Content = new StringContent(SerializePayload(normalizedPath, payload), Encoding.UTF8, "application/json");
        }

        if (requireAuthentication)
        {
            foreach (var header in BuildSignedHeaders(method, requestUri))
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Kalshi API {method.Method} {normalizedPath} failed ({(int)response.StatusCode}): {Truncate(body, 500)}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(body) ?? new JsonObject();
    }

    private Dictionary<string, string> BuildSignedHeaders(HttpMethod method, string requestUri)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKeyId))
        {
            throw new InvalidOperationException("Kalshi bridge is missing Integrations:KalshiApi:ApiKeyId.");
        }

        var pem = LoadPrivateKeyPem();
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException("Kalshi bridge is missing a Kalshi private key.");
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var message = Encoding.UTF8.GetBytes($"{timestamp}{method.Method.ToUpperInvariant()}{BuildSignaturePath(requestUri)}");

        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        var signatureBytes = rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KALSHI-ACCESS-KEY"] = _options.ApiKeyId,
            ["KALSHI-ACCESS-TIMESTAMP"] = timestamp,
            ["KALSHI-ACCESS-SIGNATURE"] = Convert.ToBase64String(signatureBytes),
        };
    }

    private string LoadPrivateKeyPem()
    {
        if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
        {
            return _options.PrivateKeyPem;
        }

        if (!string.IsNullOrWhiteSpace(_options.PrivateKeyPath))
        {
            return File.ReadAllText(_options.PrivateKeyPath);
        }

        return string.Empty;
    }

    private string BuildSignaturePath(string requestUri)
    {
        if (_httpClient.BaseAddress is null)
        {
            throw new InvalidOperationException("Kalshi bridge requires an HTTP base address before signing requests.");
        }

        var absoluteUri = new Uri(_httpClient.BaseAddress, requestUri);
        return absoluteUri.PathAndQuery.Split('?', 2)[0];
    }

    private static KeyValuePair<string, string?> CreateQueryPair(string key, string? value)
        => new(key, value);

    private static string NormalizePath(string value)
        => value.StartsWith('/') ? value : $"/{value}";

    private static string SerializePayload(string normalizedPath, JsonObject payload)
    {
        if (!string.Equals(normalizedPath, "/portfolio/orders", StringComparison.Ordinal))
        {
            return payload.ToJsonString();
        }

        var normalizedPayload = payload.DeepClone() as JsonObject ?? new JsonObject();
        NormalizeOrderPriceField(normalizedPayload, "yes_price_dollars");
        NormalizeOrderPriceField(normalizedPayload, "no_price_dollars");
        return normalizedPayload.ToJsonString();
    }

    private static void NormalizeOrderPriceField(JsonObject payload, string propertyName)
    {
        if (!payload.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return;
        }

        if (node is not JsonValue value)
        {
            return;
        }

        if (value.TryGetValue<string>(out var stringValue))
        {
            if (decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedStringValue))
            {
                payload[propertyName] = parsedStringValue.ToString("0.0000", CultureInfo.InvariantCulture);
            }

            return;
        }

        if (value.TryGetValue<decimal>(out var decimalValue))
        {
            payload[propertyName] = decimalValue.ToString("0.0000", CultureInfo.InvariantCulture);
            return;
        }

        if (value.TryGetValue<double>(out var doubleValue))
        {
            payload[propertyName] = ((decimal)doubleValue).ToString("0.0000", CultureInfo.InvariantCulture);
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
