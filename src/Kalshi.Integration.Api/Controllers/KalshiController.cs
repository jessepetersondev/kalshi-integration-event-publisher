using Asp.Versioning;
using Kalshi.Integration.Contracts.Kalshi;
using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Infrastructure.Integrations.Kalshi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

/// <summary>
/// Exposes a Kalshi-compatible bridge so legacy clients can route all Kalshi traffic through the publisher.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/kalshi")]
public sealed class KalshiController : ControllerBase
{
    private readonly KalshiBridgeService _kalshiBridgeService;

    /// <summary>
    /// Initializes a new instance of the <see cref="KalshiController"/> class.
    /// </summary>
    /// <param name="kalshiBridgeService">The bridge service that proxies Kalshi operations.</param>
    public KalshiController(KalshiBridgeService kalshiBridgeService)
    {
        _kalshiBridgeService = kalshiBridgeService;
    }

    /// <summary>
    /// Returns the raw Kalshi series payload.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("series")]
    public async Task<IActionResult> GetSeries([FromQuery] string? category = null, [FromQuery] string[]? tags = null, CancellationToken cancellationToken = default)
    {
        var payload = await _kalshiBridgeService.GetSeriesAsync(category, tags ?? Array.Empty<string>(), cancellationToken);
        return Ok(payload);
    }

    /// <summary>
    /// Returns the raw Kalshi markets payload.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("markets")]
    public async Task<IActionResult> GetMarkets(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 200,
        [FromQuery(Name = "series_ticker")] string? seriesTicker = null,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var payload = await _kalshiBridgeService.GetMarketsAsync(status, limit, seriesTicker, cursor, cancellationToken);
        return Ok(payload);
    }

    /// <summary>
    /// Returns the raw Kalshi market payload for a single ticker.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("markets/{ticker}")]
    public async Task<IActionResult> GetMarket(string ticker, CancellationToken cancellationToken = default)
    {
        var payload = await _kalshiBridgeService.GetMarketAsync(ticker, cancellationToken);
        return Ok(payload);
    }

    /// <summary>
    /// Returns the Kalshi balance payload for the configured subaccount.
    /// </summary>
    [Authorize(Policy = "trading.write")]
    [HttpGet("portfolio/balance")]
    public async Task<IActionResult> GetBalance(CancellationToken cancellationToken = default)
    {
        var payload = await _kalshiBridgeService.GetBalanceAsync(cancellationToken);
        return Ok(payload);
    }

    /// <summary>
    /// Returns the Kalshi positions payload for the configured subaccount.
    /// </summary>
    [Authorize(Policy = "trading.write")]
    [HttpGet("portfolio/positions")]
    public async Task<IActionResult> GetPositions(CancellationToken cancellationToken = default)
    {
        var payload = await _kalshiBridgeService.GetPositionsAsync(cancellationToken);
        return Ok(payload);
    }

    /// <summary>
    /// Places a Kalshi order through the bridge while creating publisher-side records.
    /// </summary>
    [Authorize(Policy = "trading.write")]
    [HttpPost("portfolio/orders")]
    public async Task<IActionResult> PlaceOrder([FromBody] SubmitKalshiOrderRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await _kalshiBridgeService.PlaceOrderAsync(request, cancellationToken);
            return Ok(payload);
        }
        catch (DomainException exception)
        {
            return Problem(title: "Invalid Kalshi bridge order", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(title: "Bridge order dependency missing", detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(title: "Kalshi bridge call failed", detail: exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Returns the latest Kalshi order payload for a publisher-tracked order.
    /// </summary>
    [Authorize(Policy = "trading.write")]
    [HttpGet("portfolio/orders/{orderId:guid}")]
    public async Task<IActionResult> GetOrder(Guid orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await _kalshiBridgeService.GetOrderAsync(orderId, cancellationToken);
            return Ok(payload);
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(title: "Order not found", detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(title: "Kalshi bridge call failed", detail: exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Cancels the Kalshi order payload for a publisher-tracked order.
    /// </summary>
    [Authorize(Policy = "trading.write")]
    [HttpDelete("portfolio/orders/{orderId:guid}")]
    public async Task<IActionResult> CancelOrder(Guid orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = await _kalshiBridgeService.CancelOrderAsync(orderId, cancellationToken);
            return Ok(payload);
        }
        catch (KeyNotFoundException exception)
        {
            return Problem(title: "Order not found", detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (InvalidOperationException exception)
        {
            return Problem(title: "Kalshi bridge call failed", detail: exception.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
