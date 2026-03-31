using System.Diagnostics;
using Kalshi.Integration.Contracts.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

/// <summary>
/// Calls the node gateway API and normalizes its readiness results for the publisher.
/// </summary>
public sealed partial class NodeGatewayClient : INodeGatewayClient
{
    private readonly HttpClient _httpClient;
    private readonly NodeGatewayOptions _options;
    private readonly ILogger<NodeGatewayClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeGatewayClient"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client configured for node gateway requests.</param>
    /// <param name="options">The node gateway integration settings.</param>
    /// <param name="logger">The logger for outbound dependency telemetry.</param>
    public NodeGatewayClient(HttpClient httpClient, IOptions<NodeGatewayOptions> options, ILogger<NodeGatewayClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<NodeGatewayProbeResult> ProbeHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var path = NormalizeHealthPath(_options.HealthPath);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var correlationId = request.Headers.TryGetValues("x-correlation-id", out var values)
                ? values.FirstOrDefault() ?? string.Empty
                : string.Empty;
            stopwatch.Stop();
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

            KalshiTelemetry.OutboundDependencyDurationMs.Record(
                elapsedMs,
                new KeyValuePair<string, object?>("server.address", _httpClient.BaseAddress?.Host ?? "node-gateway"),
                new KeyValuePair<string, object?>("url.path", path),
                new KeyValuePair<string, object?>("http.response.status_code", (int)response.StatusCode));

            OutboundDependencyCallSucceeded(_logger, "node-gateway", path, (int)response.StatusCode, elapsedMs, null);

            return new NodeGatewayProbeResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                string.IsNullOrWhiteSpace(body) ? null : body,
                correlationId,
                stopwatch.Elapsed.TotalMilliseconds,
                _httpClient.BaseAddress?.ToString() ?? _options.BaseUrl,
                path);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;

            KalshiTelemetry.OutboundDependencyDurationMs.Record(
                elapsedMs,
                new KeyValuePair<string, object?>("server.address", _httpClient.BaseAddress?.Host ?? "node-gateway"),
                new KeyValuePair<string, object?>("url.path", path),
                new KeyValuePair<string, object?>("error.type", exception.GetType().Name));

            OutboundDependencyCallFailed(_logger, "node-gateway", path, elapsedMs, exception);
            throw;
        }
    }

    private static string NormalizeHealthPath(string value)
    {
        return value.StartsWith('/') ? value : $"/{value}";
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Outbound dependency call {dependency} {operation} returned statusCode={statusCode} in {durationMs} ms.")]
    private static partial void OutboundDependencyCallSucceeded(ILogger logger, string dependency, string operation, int statusCode, double durationMs, Exception? exception);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "Outbound dependency call {dependency} {operation} failed in {durationMs} ms.")]
    private static partial void OutboundDependencyCallFailed(ILogger logger, string dependency, string operation, double durationMs, Exception exception);
}
