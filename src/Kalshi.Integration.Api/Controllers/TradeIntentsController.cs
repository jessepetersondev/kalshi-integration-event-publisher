using System.Globalization;
using Asp.Versioning;
using Kalshi.Integration.Api.Infrastructure;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.TradeIntents;
using Kalshi.Integration.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/trade-intents")]
public sealed class TradeIntentsController : ControllerBase
{
    private const string IdempotencyScope = "trade-intents";

    private readonly TradingService _tradingService;
    private readonly IOperationalIssueStore _issueStore;
    private readonly IAuditRecordStore _auditRecordStore;
    private readonly IApplicationEventPublisher _applicationEventPublisher;
    private readonly IdempotencyService _idempotencyService;
    private readonly ILogger<TradeIntentsController> _logger;

    public TradeIntentsController(
        TradingService tradingService,
        IOperationalIssueStore issueStore,
        IAuditRecordStore auditRecordStore,
        IApplicationEventPublisher applicationEventPublisher,
        IdempotencyService idempotencyService,
        ILogger<TradeIntentsController> logger)
    {
        _tradingService = tradingService;
        _issueStore = issueStore;
        _auditRecordStore = auditRecordStore;
        _applicationEventPublisher = applicationEventPublisher;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    [HttpPost]
    [Authorize(Policy = "trading.write")]
    [ProducesResponseType(typeof(TradeIntentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateTradeIntentRequest request, CancellationToken cancellationToken)
    {
        var correlationId = RequestMetadata.ResolveCorrelationId(HttpContext, request.CorrelationId);
        var idempotencyKey = RequestMetadata.ResolveIdempotencyKey(HttpContext);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["IdempotencyKey"] = idempotencyKey,
        });

        var replay = await _idempotencyService.LookupAsync(IdempotencyScope, idempotencyKey, request, cancellationToken);
        if (replay.Status == IdempotencyLookupStatus.Conflict)
        {
            _logger.LogWarning("Rejected trade intent request because idempotency key {IdempotencyKey} was reused with a different payload.", idempotencyKey);
            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "idempotency",
                    action: "trade_intent.conflict",
                    outcome: "rejected",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"ticker={request.Ticker}; side={request.Side}; quantity={request.Quantity}"),
                cancellationToken);

            return Problem(
                title: "Idempotency key conflict",
                detail: $"Idempotency key '{idempotencyKey}' was already used for a different trade intent request.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (replay.Status == IdempotencyLookupStatus.Replay && replay.Record is not null)
        {
            RequestMetadata.MarkReplay(HttpContext);
            _logger.LogInformation("Replayed trade intent response for idempotency key {IdempotencyKey}.", idempotencyKey);
            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "idempotency",
                    action: "trade_intent.replayed",
                    outcome: "replayed",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"ticker={request.Ticker}; side={request.Side}; quantity={request.Quantity}"),
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
            var response = await _tradingService.CreateTradeIntentAsync(request, cancellationToken);
            _logger.LogInformation("Created trade intent {TradeIntentId} for {Ticker}.", response.Id, response.Ticker);

            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "trading",
                    action: "trade_intent.created",
                    outcome: "success",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    resourceId: response.Id.ToString(),
                    details: $"ticker={response.Ticker}; side={response.Side}; quantity={response.Quantity}; strategy={response.StrategyName}"),
                cancellationToken);

            await _applicationEventPublisher.PublishAsync(
                ApplicationEventEnvelope.Create(
                    category: "trading",
                    name: "trade-intent.created",
                    resourceId: response.Id.ToString(),
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    attributes: new Dictionary<string, string?>
                    {
                        ["ticker"] = response.Ticker,
                        ["side"] = response.Side,
                        ["quantity"] = response.Quantity.ToString(CultureInfo.InvariantCulture),
                        ["strategyName"] = response.StrategyName,
                    }),
                cancellationToken);

            await _idempotencyService.SaveResponseAsync(IdempotencyScope, idempotencyKey, request, StatusCodes.Status201Created, response, cancellationToken);
            return CreatedAtAction(nameof(Create), new { id = response.Id, version = "1" }, response);
        }
        catch (DomainException exception)
        {
            _logger.LogWarning(exception, "Rejected trade intent for {Ticker}.", request.Ticker);

            await _issueStore.AddAsync(
                OperationalIssue.Create(
                    category: "validation",
                    severity: "warning",
                    source: "trade-intents",
                    message: exception.Message,
                    details: $"ticker={request.Ticker}; side={request.Side}; quantity={request.Quantity}; correlationId={request.CorrelationId}"),
                cancellationToken);

            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "validation",
                    action: "trade_intent.rejected",
                    outcome: "rejected",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"ticker={request.Ticker}; side={request.Side}; quantity={request.Quantity}; reason={exception.Message}"),
                cancellationToken);

            return Problem(title: "Invalid trade intent", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
