using Asp.Versioning;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Positions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

/// <summary>
/// Exposes HTTP endpoints for positions.
/// </summary>


[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/positions")]
public sealed class PositionsController : ControllerBase
{
    private readonly TradingQueryService _tradingQueryService;

    public PositionsController(TradingQueryService tradingQueryService)
    {
        _tradingQueryService = tradingQueryService;
    }

    [HttpGet]
    [Authorize(Policy = "operations.read")]
    [ProducesResponseType(typeof(IReadOnlyList<PositionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var positions = await _tradingQueryService.GetPositionsAsync(cancellationToken);
        return Ok(positions);
    }
}
