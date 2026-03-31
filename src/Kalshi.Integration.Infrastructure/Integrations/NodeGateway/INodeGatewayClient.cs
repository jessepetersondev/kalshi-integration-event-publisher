namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

/// <summary>
/// Defines the node gateway operations required by the API and health checks.
/// </summary>
public interface INodeGatewayClient
{
    Task<NodeGatewayProbeResult> ProbeHealthAsync(CancellationToken cancellationToken = default);
}
