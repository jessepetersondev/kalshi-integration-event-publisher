namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

/// <summary>
/// Represents the result of node gateway probe.
/// </summary>


public sealed record NodeGatewayProbeResult(
    bool Healthy,
    int StatusCode,
    string? ResponseBody,
    string CorrelationId,
    double DurationMs,
    string BaseUrl,
    string Path);
