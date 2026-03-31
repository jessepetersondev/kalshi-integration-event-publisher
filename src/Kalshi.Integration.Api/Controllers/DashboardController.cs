using Asp.Versioning;
using Kalshi.Integration.Application.Dashboard;
using Kalshi.Integration.Contracts.Dashboard;
using Kalshi.Integration.Contracts.Positions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

/// <summary>
/// Exposes operator-facing read models for orders, positions, events, issues,
/// and audit history without mixing that query logic into write controllers.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dashboard")]
[Authorize(Policy = "operations.read")]
public sealed class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboardService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardController"/> class.
    /// </summary>
    /// <param name="dashboardService">The service that assembles dashboard read models.</param>
    public DashboardController(DashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Returns the current order summaries used by the operator dashboard.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The current set of tracked orders.</returns>
    [HttpGet("orders")]
    [ProducesResponseType(typeof(IReadOnlyList<DashboardOrderSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrders(CancellationToken cancellationToken)
    {
        var orders = await _dashboardService.GetOrdersAsync(cancellationToken);
        return Ok(orders);
    }

    /// <summary>
    /// Returns the latest position snapshot for each tracked market and side.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The current portfolio positions.</returns>
    [HttpGet("positions")]
    [ProducesResponseType(typeof(IReadOnlyList<PositionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPositions(CancellationToken cancellationToken)
    {
        var positions = await _dashboardService.GetPositionsAsync(cancellationToken);
        return Ok(positions);
    }

    /// <summary>
    /// Returns recent execution and order-status events for dashboard timelines.
    /// </summary>
    /// <param name="limit">The maximum number of events to return.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The most recent dashboard events up to the requested limit.</returns>
    [HttpGet("events")]
    [ProducesResponseType(typeof(IReadOnlyList<DashboardEventResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEvents([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var events = await _dashboardService.GetEventsAsync(Math.Clamp(limit, 1, 200), cancellationToken);
        return Ok(events);
    }

    /// <summary>
    /// Returns recent operational issues, optionally filtered by category and time window.
    /// </summary>
    /// <param name="category">The optional issue category to filter on.</param>
    /// <param name="hours">The rolling lookback window in hours.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The issues visible to the operations dashboard.</returns>
    [HttpGet("issues")]
    [ProducesResponseType(typeof(IReadOnlyList<DashboardIssueResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIssues([FromQuery] string? category = null, [FromQuery] int hours = 24, CancellationToken cancellationToken = default)
    {
        var issues = await _dashboardService.GetIssuesAsync(category, Math.Clamp(hours, 1, 168), cancellationToken);
        return Ok(issues);
    }

    /// <summary>
    /// Returns recent audit records that explain what the API accepted, rejected, or replayed.
    /// </summary>
    /// <param name="category">The optional audit category to filter on.</param>
    /// <param name="hours">The rolling lookback window in hours.</param>
    /// <param name="limit">The maximum number of records to return.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The matching audit records in reverse chronological order.</returns>
    [HttpGet("audit-records")]
    [ProducesResponseType(typeof(IReadOnlyList<DashboardAuditRecordResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAuditRecords([FromQuery] string? category = null, [FromQuery] int hours = 24, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        var records = await _dashboardService.GetAuditRecordsAsync(category, Math.Clamp(hours, 1, 168), Math.Clamp(limit, 1, 500), cancellationToken);
        return Ok(records);
    }
}
