using System.Globalization;
using Asp.Versioning;
using Kalshi.Integration.Api.Infrastructure;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Orders;
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

            await _applicationEventPublisher.PublishAsync(
                ApplicationEventEnvelope.Create(
                    category: "trading",
                    name: "order.created",
                    resourceId: response.Id.ToString(),
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    attributes: new Dictionary<string, string?>
                    {
                        ["tradeIntentId"] = response.TradeIntentId.ToString(),
                        ["ticker"] = response.Ticker,
                        ["side"] = response.Side,
                        ["quantity"] = response.Quantity.ToString(CultureInfo.InvariantCulture),
                        ["limitPrice"] = response.LimitPrice.ToString(CultureInfo.InvariantCulture),
                        ["status"] = response.Status,
                    }),
                cancellationToken);

            await _idempotencyService.SaveResponseAsync(IdempotencyScope, idempotencyKey, request, StatusCodes.Status201Created, response, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = response.Id, version = "1" }, response);
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
    }

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
