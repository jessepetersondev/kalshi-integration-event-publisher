using System.Globalization;
using Asp.Versioning;
using Kalshi.Integration.Api.Infrastructure;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Infrastructure.Messaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

/// <summary>
/// Handles order creation and retrieval endpoints, including idempotency checks,
/// audit records, and application-event publication.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/orders")]
public sealed class OrdersController : ControllerBase
{
    private const string IdempotencyScope = "orders";

    private readonly TradingService _tradingService;
    private readonly TradingQueryService _tradingQueryService;
    private readonly IAuditRecordStore _auditRecordStore;
    private readonly IApplicationEventPublisher _applicationEventPublisher;
    private readonly IdempotencyService _idempotencyService;
    private readonly ILogger<OrdersController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersController"/> class.
    /// </summary>
    /// <param name="tradingService">The service that creates orders.</param>
    /// <param name="tradingQueryService">The service that reads order projections.</param>
    /// <param name="auditRecordStore">The store used to persist audit records.</param>
    /// <param name="applicationEventPublisher">The publisher used to emit order events.</param>
    /// <param name="idempotencyService">The service used to detect duplicate order submissions.</param>
    /// <param name="logger">The logger for the controller.</param>
    public OrdersController(
        TradingService tradingService,
        TradingQueryService tradingQueryService,
        IAuditRecordStore auditRecordStore,
        IApplicationEventPublisher applicationEventPublisher,
        IdempotencyService idempotencyService,
        ILogger<OrdersController> logger)
    {
        _tradingService = tradingService;
        _tradingQueryService = tradingQueryService;
        _auditRecordStore = auditRecordStore;
        _applicationEventPublisher = applicationEventPublisher;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    /// <summary>
    /// Creates an order for an existing trade intent.
    /// </summary>
    /// <param name="request">The order request payload.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>A created response containing the persisted order.</returns>
    [HttpPost]
    [Authorize(Policy = "trading.write")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var correlationId = RequestMetadata.ResolveCorrelationId(HttpContext);
        var idempotencyKey = RequestMetadata.ResolveIdempotencyKey(HttpContext);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["IdempotencyKey"] = idempotencyKey,
        });

        var replay = await _idempotencyService.LookupAsync(IdempotencyScope, idempotencyKey, request, cancellationToken);
        if (replay.Status == IdempotencyLookupStatus.Conflict)
        {
            _logger.LogWarning("Rejected order request because idempotency key {IdempotencyKey} was reused with a different payload.", idempotencyKey);
            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "idempotency",
                    action: "order.conflict",
                    outcome: "rejected",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"tradeIntentId={request.TradeIntentId}"),
                cancellationToken);

            return Problem(
                title: "Idempotency key conflict",
                detail: $"Idempotency key '{idempotencyKey}' was already used for a different order request.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (replay.Status == IdempotencyLookupStatus.Replay && replay.Record is not null)
        {
            RequestMetadata.MarkReplay(HttpContext);
            _logger.LogInformation("Replayed order response for idempotency key {IdempotencyKey}.", idempotencyKey);
            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "idempotency",
                    action: "order.replayed",
                    outcome: "replayed",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"tradeIntentId={request.TradeIntentId}"),
                cancellationToken);

            return new ContentResult
            {
                StatusCode = replay.Record.StatusCode,
                ContentType = "application/json",
                Content = replay.Record.ResponseBody,
            };
        }

        try
        {
            var response = await _tradingService.CreateOrderAsync(request, cancellationToken);
            _logger.LogInformation("Created order {OrderId} for trade intent {TradeIntentId}.", response.Id, response.TradeIntentId);

            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "trading",
                    action: "order.created",
                    outcome: "success",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    resourceId: response.Id.ToString(),
                    details: $"tradeIntentId={response.TradeIntentId}; ticker={response.Ticker}; quantity={response.Quantity}; status={response.Status}"),
                cancellationToken);

            var commandEvent = WeatherQuantCommandMapper.CreateOrderEvent(response, correlationId, idempotencyKey);
            await _tradingService.MarkOrderPublishAttemptedAsync(response.Id, commandEvent.OccurredAt, cancellationToken);

            try
            {
                await _applicationEventPublisher.PublishAsync(commandEvent, cancellationToken);
                await _tradingService.MarkOrderPublishConfirmedAsync(response.Id, commandEvent.Id, DateTimeOffset.UtcNow, cancellationToken);
            }
            catch (PublishConfirmationException exception)
            {
                await _tradingService.MarkOrderPublishPendingReviewAsync(response.Id, exception.Message, commandEvent.Id, DateTimeOffset.UtcNow, cancellationToken);
            }

            await _idempotencyService.SaveResponseAsync(IdempotencyScope, idempotencyKey, request, StatusCodes.Status201Created, response, cancellationToken);
            var refreshed = await _tradingQueryService.GetOrderAsync(response.Id, cancellationToken) ?? response;
            return CreatedAtAction(nameof(GetById), new { id = refreshed.Id, version = "1" }, refreshed);
        }
        catch (KeyNotFoundException exception)
        {
            _logger.LogWarning(exception, "Failed to create order for trade intent {TradeIntentId}.", request.TradeIntentId);
            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "trading",
                    action: "order.rejected",
                    outcome: "rejected",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"tradeIntentId={request.TradeIntentId}; reason={exception.Message}"),
                cancellationToken);

            return Problem(title: "Trade intent not found", detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (DomainException exception)
        {
            _logger.LogWarning(exception, "Rejected duplicate order creation for trade intent {TradeIntentId}.", request.TradeIntentId);
            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "trading",
                    action: "order.conflict",
                    outcome: "rejected",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"tradeIntentId={request.TradeIntentId}; reason={exception.Message}"),
                cancellationToken);

            return Problem(title: "Order already exists", detail: exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <summary>
    /// Returns the current execution outcome view for tracked orders, optionally filtered by
    /// publisher/client correlation and normalized result state.
    /// </summary>
    /// <param name="orderId">The optional order identifier.</param>
    /// <param name="correlationId">The optional client correlation identifier.</param>
    /// <param name="originService">The optional calling client/service name.</param>
    /// <param name="status">The optional raw order status filter.</param>
    /// <param name="publishStatus">The optional raw publish-status filter.</param>
    /// <param name="outcomeState">The optional normalized outcome-state filter.</param>
    /// <param name="resultStatus">The optional raw executor result-status filter.</param>
    /// <param name="limit">The maximum number of rows to return.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The matching execution-outcome rows.</returns>
    [HttpGet("outcomes")]
    [Authorize(Policy = "trading.read")]
    [ProducesResponseType(typeof(IReadOnlyList<OrderOutcomeResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOutcomes(
        [FromQuery] Guid? orderId = null,
        [FromQuery] string? correlationId = null,
        [FromQuery] string? originService = null,
        [FromQuery] string? status = null,
        [FromQuery] string? publishStatus = null,
        [FromQuery] string? outcomeState = null,
        [FromQuery] string? resultStatus = null,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var outcomes = await _tradingQueryService.GetOrderOutcomesAsync(
            orderId,
            correlationId,
            originService,
            status,
            publishStatus,
            outcomeState,
            resultStatus,
            limit,
            cancellationToken);

        return Ok(outcomes);
    }

    /// <summary>
    /// Returns the latest projection for a single order.
    /// </summary>
    /// <param name="id">The order identifier.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>The order projection when it exists.</returns>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = "trading.read")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var order = await _tradingQueryService.GetOrderAsync(id, cancellationToken);
        if (order is null)
        {
            return Problem(title: "Order not found", detail: $"Order '{id}' was not found.", statusCode: StatusCodes.Status404NotFound);
        }

        return Ok(order);
    }
}
