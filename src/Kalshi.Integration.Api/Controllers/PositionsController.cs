using Asp.Versioning;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Positions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

/// <summary>
/// Returns operator-facing position snapshots derived from execution state.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PositionsController"/> class.
/// </remarks>
/// <param name="tradingQueryService">The service that reads position projections.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/positions")]
public sealed class PositionsController(TradingQueryService tradingQueryService) : ControllerBase
{
    private readonly TradingQueryService _tradingQueryService = tradingQueryService;

    /// <summary>
    /// Returns the latest position snapshot for each tracked ticker and side.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The current portfolio positions.</returns>
    [HttpGet]
    [Authorize(Policy = "operations.read")]
    [ProducesResponseType(typeof(IReadOnlyList<PositionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        IReadOnlyList<PositionResponse> positions = await _tradingQueryService.GetPositionsAsync(cancellationToken);
        return Ok(positions);
    }
}
