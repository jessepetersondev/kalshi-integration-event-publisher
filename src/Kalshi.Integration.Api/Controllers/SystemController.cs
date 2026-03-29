using Asp.Versioning;
using Kalshi.Integration.Infrastructure.Integrations.NodeGateway;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/system")]
public sealed class SystemController : ControllerBase
{
    private readonly INodeGatewayClient _nodeGatewayClient;

    public SystemController(INodeGatewayClient nodeGatewayClient)
    {
        _nodeGatewayClient = nodeGatewayClient;
    }

    [HttpGet("ping")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Ping()
    {
        return Ok(new
        {
            status = "ok",
            service = "kalshi-integration-sandbox",
            utc = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("dependencies/node-gateway")]
    [Authorize(Policy = "operations.read")]
    [ProducesResponseType(typeof(NodeGatewayProbeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ProbeNodeGateway(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _nodeGatewayClient.ProbeHealthAsync(cancellationToken);
            return result.Healthy
                ? Ok(result)
                : Problem(
                    title: "Node gateway probe failed",
                    detail: $"Node gateway responded with status code {result.StatusCode}.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception)
        {
            return Problem(
                title: "Node gateway probe failed",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
