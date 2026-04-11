using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

/// <summary>
/// Reports health for node gateway readiness.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="NodeGatewayReadinessHealthCheck"/> class.
/// </remarks>
/// <param name="client">The client used to probe node gateway readiness.</param>
public sealed class NodeGatewayReadinessHealthCheck(INodeGatewayClient client) : IHealthCheck
{
    private readonly INodeGatewayClient _client = client;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            NodeGatewayProbeResult result = await _client.ProbeHealthAsync(cancellationToken);
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
