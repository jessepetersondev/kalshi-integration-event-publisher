namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

/// <summary>
/// Represents the outcome of probing the node gateway dependency.
/// </summary>
public sealed record NodeGatewayProbeResult(
    bool Healthy,
    int StatusCode,
    string? ResponseBody,
    string CorrelationId,
    double DurationMs,
    string BaseUrl,
    string Path);
