namespace Kalshi.Integration.Infrastructure.Integrations.NodeGateway;

public interface INodeGatewayClient
{
    Task<NodeGatewayProbeResult> ProbeHealthAsync(CancellationToken cancellationToken = default);
}
