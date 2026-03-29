using Asp.Versioning;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Positions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/positions")]
public sealed class PositionsController : ControllerBase
{
    private readonly TradingService _tradingService;

    public PositionsController(TradingService tradingService)
    {
        _tradingService = tradingService;
    }

    [HttpGet]
    [Authorize(Policy = "operations.read")]
    [ProducesResponseType(typeof(IReadOnlyList<PositionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var positions = await _tradingService.GetPositionsAsync(cancellationToken);
        return Ok(positions);
    }
}
