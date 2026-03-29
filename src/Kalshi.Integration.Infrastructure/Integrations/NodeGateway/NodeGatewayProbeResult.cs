namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

public sealed record NodeGatewayProbeResult(
    bool Healthy,
    int StatusCode,
    string? ResponseBody,
    string CorrelationId,
    double DurationMs,
    string BaseUrl,
    string Path);
