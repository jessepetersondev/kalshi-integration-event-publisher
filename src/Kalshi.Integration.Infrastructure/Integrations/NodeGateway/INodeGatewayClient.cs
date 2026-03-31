namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

/// <summary>
/// Provides access to i node gateway.
/// </summary>


public interface INodeGatewayClient
{
    Task<NodeGatewayProbeResult> ProbeHealthAsync(CancellationToken cancellationToken = default);
}
