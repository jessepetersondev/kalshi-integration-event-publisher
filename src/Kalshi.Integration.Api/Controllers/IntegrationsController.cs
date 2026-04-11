using System.Globalization;
using Asp.Versioning;
using Kalshi.Integration.Api.Infrastructure;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Integrations;
using Kalshi.Integration.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kalshi.Integration.Api.Controllers;

/// <summary>
/// Accepts callbacks from external systems and records their effects as audited,
/// idempotent application events.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="IntegrationsController"/> class.
/// </remarks>
/// <param name="tradingService">The trading workflow service.</param>
/// <param name="issueStore">The store used to record operational issues.</param>
/// <param name="auditRecordStore">The store used to persist audit records.</param>
/// <param name="applicationEventPublisher">The publisher used to emit integration events.</param>
/// <param name="idempotencyService">The service used to prevent duplicate callback processing.</param>
/// <param name="logger">The logger for the controller.</param>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/integrations")]
public sealed class IntegrationsController(
    TradingService tradingService,
    IOperationalIssueStore issueStore,
    IAuditRecordStore auditRecordStore,
    IApplicationEventPublisher applicationEventPublisher,
    IdempotencyService idempotencyService,
    ILogger<IntegrationsController> logger) : ControllerBase
{
    private const string IdempotencyScope = "execution-updates";

    private readonly TradingService _tradingService = tradingService;
    private readonly IOperationalIssueStore _issueStore = issueStore;
    private readonly IAuditRecordStore _auditRecordStore = auditRecordStore;
    private readonly IApplicationEventPublisher _applicationEventPublisher = applicationEventPublisher;
    private readonly IdempotencyService _idempotencyService = idempotencyService;
    private readonly ILogger<IntegrationsController> _logger = logger;

    /// <summary>
    /// Applies an execution update produced by an external execution system.
    /// </summary>
    /// <param name="request">The execution update payload to apply.</param>
    /// <param name="cancellationToken">The cancellation token for the request.</param>
    /// <returns>An accepted response containing the resulting order snapshot.</returns>
    [HttpPost("execution-updates")]
    [Authorize(Policy = "integration.write")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReceiveExecutionUpdate([FromBody] ExecutionUpdateRequest request, CancellationToken cancellationToken)
    {
        string correlationId = RequestMetadata.ResolveCorrelationId(HttpContext, request.CorrelationId);
        string inboundReplayKey = !string.IsNullOrWhiteSpace(request.CorrelationId)
            ? request.CorrelationId
            : $"{request.OrderId:N}:{request.Status.Trim().ToLowerInvariant()}:{request.FilledQuantity.ToString(CultureInfo.InvariantCulture)}:{request.OccurredAt?.ToUniversalTime().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) ?? "none"}";
        string? idempotencyKey = RequestMetadata.ResolveIdempotencyKey(HttpContext, inboundReplayKey);

        using IDisposable? scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["IdempotencyKey"] = idempotencyKey,
        });

        IdempotencyLookupResult replay = await _idempotencyService.LookupAsync(IdempotencyScope, idempotencyKey, request, cancellationToken);
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
            ExecutionUpdateResult result = await _tradingService.ApplyExecutionUpdateAsync(request, cancellationToken);
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
