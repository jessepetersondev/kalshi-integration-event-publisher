using Asp.Versioning;
using Kalshi.Integration.Infrastructure.Integrations.NodeGateway;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

/// <summary>
/// Exposes low-level operational endpoints used for service liveness and dependency checks.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/system")]
public sealed class SystemController : ControllerBase
{
    private readonly INodeGatewayClient _nodeGatewayClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemController"/> class.
    /// </summary>
    /// <param name="nodeGatewayClient">The client used to probe the node gateway dependency.</param>
    public SystemController(INodeGatewayClient nodeGatewayClient)
    {
        _nodeGatewayClient = nodeGatewayClient;
    }

    /// <summary>
    /// Returns a lightweight liveness payload for infrastructure health probes.
    /// </summary>
    /// <returns>A simple payload confirming that the service is running.</returns>
    [HttpGet("ping")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Ping()
    {
        return Ok(new
        {
            status = "ok",
            service = "kalshi-integration-event-publisher",
            utc = DateTimeOffset.UtcNow
        });
    }

    /// <summary>
    /// Probes the node gateway dependency and reports whether it is currently healthy.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The probe result when the dependency is healthy; otherwise a problem response.</returns>
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
