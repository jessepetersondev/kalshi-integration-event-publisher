using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

/// <summary>
/// Reports health for node gateway readiness.
/// </summary>


public sealed class NodeGatewayReadinessHealthCheck : IHealthCheck
{
    private readonly INodeGatewayClient _client;

    public NodeGatewayReadinessHealthCheck(INodeGatewayClient client)
    {
        _client = client;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _client.ProbeHealthAsync(cancellationToken);
            return result.Healthy
                ? HealthCheckResult.Healthy($"Node gateway responded with status {result.StatusCode}.")
                : HealthCheckResult.Unhealthy($"Node gateway responded with status {result.StatusCode}.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Node gateway probe failed.", exception);
        }
    }
}
