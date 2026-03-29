using Asp.Versioning;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Contracts.TradeIntents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/risk")]
public sealed class RiskController : ControllerBase
{
    private readonly RiskEvaluator _riskEvaluator;

    public RiskController(RiskEvaluator riskEvaluator)
    {
        _riskEvaluator = riskEvaluator;
    }

    [HttpPost("validate")]
    [Authorize(Policy = "trading.write")]
    [ProducesResponseType(typeof(RiskDecisionResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Validate([FromBody] CreateTradeIntentRequest request, CancellationToken cancellationToken)
    {
        var result = await _riskEvaluator.EvaluateTradeIntentAsync(request, cancellationToken);
        return Ok(new RiskDecisionResponse(
            result.Accepted,
            result.Decision,
            result.Reasons.ToArray(),
            result.MaxOrderSize,
            result.DuplicateCorrelationIdDetected));
    }
}
