using System.Globalization;
using Asp.Versioning;
using Kalshi.Integration.Api.Infrastructure;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Integrations;
using Kalshi.Integration.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/integrations")]
public sealed class IntegrationsController : ControllerBase
{
    private const string IdempotencyScope = "execution-updates";

    private readonly TradingService _tradingService;
    private readonly IOperationalIssueStore _issueStore;
    private readonly IAuditRecordStore _auditRecordStore;
    private readonly IApplicationEventPublisher _applicationEventPublisher;
    private readonly IdempotencyService _idempotencyService;
    private readonly ILogger<IntegrationsController> _logger;

    public IntegrationsController(
        TradingService tradingService,
        IOperationalIssueStore issueStore,
        IAuditRecordStore auditRecordStore,
        IApplicationEventPublisher applicationEventPublisher,
        IdempotencyService idempotencyService,
        ILogger<IntegrationsController> logger)
    {
        _tradingService = tradingService;
        _issueStore = issueStore;
        _auditRecordStore = auditRecordStore;
        _applicationEventPublisher = applicationEventPublisher;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    [HttpPost("execution-updates")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReceiveExecutionUpdate([FromBody] ExecutionUpdateRequest request, CancellationToken cancellationToken)
    {
        var correlationId = RequestMetadata.ResolveCorrelationId(HttpContext, request.CorrelationId);
        var inboundReplayKey = !string.IsNullOrWhiteSpace(request.CorrelationId)
            ? request.CorrelationId
            : $"{request.OrderId:N}:{request.Status.Trim().ToLowerInvariant()}:{request.FilledQuantity.ToString(CultureInfo.InvariantCulture)}:{request.OccurredAt?.ToUniversalTime().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? "none"}";
        var idempotencyKey = RequestMetadata.ResolveIdempotencyKey(HttpContext, inboundReplayKey);

        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["IdempotencyKey"] = idempotencyKey,
        });

        var replay = await _idempotencyService.LookupAsync(IdempotencyScope, idempotencyKey, request, cancellationToken);
        if (replay.Status == IdempotencyLookupStatus.Conflict)
        {
            _logger.LogWarning("Rejected execution update because idempotency key {IdempotencyKey} was reused with a different payload.", idempotencyKey);
            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "idempotency",
                    action: "execution_update.conflict",
                    outcome: "rejected",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"orderId={request.OrderId}; status={request.Status}; filledQuantity={request.FilledQuantity}"),
                cancellationToken);

            return Problem(
                title: "Idempotency key conflict",
                detail: $"Idempotency key '{idempotencyKey}' was already used for a different execution update.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (replay.Status == IdempotencyLookupStatus.Replay && replay.Record is not null)
        {
            RequestMetadata.MarkReplay(HttpContext);
            _logger.LogInformation("Replayed execution update response for idempotency key {IdempotencyKey}.", idempotencyKey);
            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "idempotency",
                    action: "execution_update.replayed",
                    outcome: "replayed",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"orderId={request.OrderId}; status={request.Status}; filledQuantity={request.FilledQuantity}"),
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
            var result = await _tradingService.ApplyExecutionUpdateAsync(request, cancellationToken);
            var payload = new
            {
                result.OrderId,
                result.Status,
                result.FilledQuantity,
                result.OccurredAt,
                order = result.Order,
            };

            _logger.LogInformation("Applied execution update for order {OrderId} to status {Status}.", result.OrderId, result.Status);

            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "integration",
                    action: "execution_update.applied",
                    outcome: "success",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    resourceId: result.OrderId.ToString(),
                    details: $"orderId={result.OrderId}; status={result.Status}; filledQuantity={result.FilledQuantity}"),
                cancellationToken);

            await _applicationEventPublisher.PublishAsync(
                ApplicationEventEnvelope.Create(
                    category: "integration",
                    name: "execution-update.applied",
                    resourceId: result.OrderId.ToString(),
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    attributes: new Dictionary<string, string?>
                    {
                        ["orderId"] = result.OrderId.ToString(),
                        ["status"] = result.Status,
                        ["filledQuantity"] = result.FilledQuantity.ToString(CultureInfo.InvariantCulture),
                        ["occurredAt"] = result.OccurredAt.ToString("O"),
                    }),
                cancellationToken);

            await _idempotencyService.SaveResponseAsync(IdempotencyScope, idempotencyKey, request, StatusCodes.Status202Accepted, payload, cancellationToken);
            return Accepted(payload);
        }
        catch (DomainException exception)
        {
            _logger.LogWarning(exception, "Rejected execution update for order {OrderId}.", request.OrderId);

            await _issueStore.AddAsync(
                OperationalIssue.Create(
                    category: "integration",
                    severity: "error",
                    source: "execution-updates",
                    message: exception.Message,
                    details: $"orderId={request.OrderId}; status={request.Status}; filledQuantity={request.FilledQuantity}"),
                cancellationToken);

            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "integration",
                    action: "execution_update.rejected",
                    outcome: "rejected",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"orderId={request.OrderId}; status={request.Status}; filledQuantity={request.FilledQuantity}; reason={exception.Message}"),
                cancellationToken);

            return Problem(title: "Invalid execution update", detail: exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (KeyNotFoundException exception)
        {
            _logger.LogWarning(exception, "Execution update referenced missing order {OrderId}.", request.OrderId);

            await _issueStore.AddAsync(
                OperationalIssue.Create(
                    category: "integration",
                    severity: "error",
                    source: "execution-updates",
                    message: exception.Message,
                    details: $"orderId={request.OrderId}; status={request.Status}; filledQuantity={request.FilledQuantity}"),
                cancellationToken);

            await _auditRecordStore.AddAsync(
                AuditRecord.Create(
                    category: "integration",
                    action: "execution_update.rejected",
                    outcome: "rejected",
                    correlationId: correlationId,
                    idempotencyKey: idempotencyKey,
                    details: $"orderId={request.OrderId}; status={request.Status}; filledQuantity={request.FilledQuantity}; reason={exception.Message}"),
                cancellationToken);

            return Problem(title: "Order not found", detail: exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
    }
}
