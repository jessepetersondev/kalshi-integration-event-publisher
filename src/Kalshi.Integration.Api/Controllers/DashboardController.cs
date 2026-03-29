using Asp.Versioning;
using Kalshi.Integration.Application.Dashboard;
using Kalshi.Integration.Contracts.Dashboard;
using Kalshi.Integration.Contracts.Positions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize(Policy = "operations.read")]
public sealed class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboardService;

    public DashboardController(DashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("orders")]
    [ProducesResponseType(typeof(IReadOnlyList<DashboardOrderSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders(CancellationToken cancellationToken)
    {
        var orders = await _dashboardService.GetOrdersAsync(cancellationToken);
        return Ok(orders);
    }

    [HttpGet("positions")]
    [ProducesResponseType(typeof(IReadOnlyList<PositionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPositions(CancellationToken cancellationToken)
    {
        var positions = await _dashboardService.GetPositionsAsync(cancellationToken);
        return Ok(positions);
    }

    [HttpGet("events")]
    [ProducesResponseType(typeof(IReadOnlyList<DashboardEventResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var events = await _dashboardService.GetEventsAsync(Math.Clamp(limit, 1, 200), cancellationToken);
        return Ok(events);
    }

    [HttpGet("issues")]
    [ProducesResponseType(typeof(IReadOnlyList<DashboardIssueResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIssues([FromQuery] string? category = null, [FromQuery] int hours = 24, CancellationToken cancellationToken = default)
    {
        var issues = await _dashboardService.GetIssuesAsync(category, Math.Clamp(hours, 1, 168), cancellationToken);
        return Ok(issues);
    }

    [HttpGet("audit-records")]
    [ProducesResponseType(typeof(IReadOnlyList<DashboardAuditRecordResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditRecords([FromQuery] string? category = null, [FromQuery] int hours = 24, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var records = await _dashboardService.GetAuditRecordsAsync(category, Math.Clamp(hours, 1, 168), Math.Clamp(limit, 1, 500), cancellationToken);
        return Ok(records);
    }
}
