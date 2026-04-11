using Asp.Versioning;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Contracts.TradeIntents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

/// <summary>
/// Evaluates proposed trade intents against the publisher's configured risk controls.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RiskController"/> class.
/// </remarks>
/// <param name="riskEvaluator">The service that evaluates trade-intent risk.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/risk")]
public sealed class RiskController(RiskEvaluator riskEvaluator) : ControllerBase
{
    private readonly RiskEvaluator _riskEvaluator = riskEvaluator;

    /// <summary>
    /// Validates a proposed trade intent without creating any trading state.
    /// </summary>
    /// <param name="request">The trade intent to evaluate.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The risk decision that would be applied to the request.</returns>
    [HttpPost("validate")]
    [Authorize(Policy = "trading.write")]
    [ProducesResponseType(typeof(RiskDecisionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Validate([FromBody] CreateTradeIntentRequest request, CancellationToken cancellationToken)
    {
        RiskDecision result = await _riskEvaluator.EvaluateTradeIntentAsync(request, cancellationToken);
        return Ok(new RiskDecisionResponse(
            result.Accepted,
            result.Decision,
            [.. result.Reasons],
            result.MaxOrderSize,
            result.DuplicateCorrelationIdDetected));
    }
}
