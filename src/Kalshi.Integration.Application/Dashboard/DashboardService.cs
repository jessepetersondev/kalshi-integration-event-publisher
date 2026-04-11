using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.Dashboard;
using Kalshi.Integration.Contracts.Positions;

namespace Kalshi.Integration.Application.Dashboard;

/// <summary>
/// Builds the operator-facing read models used by the dashboard endpoints.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="DashboardService"/> class.
/// </remarks>
/// <param name="orderRepository">The repository used to read orders and execution history.</param>
/// <param name="positionSnapshotRepository">The repository used to read position snapshots.</param>
/// <param name="issueStore">The store used to read recent operational issues.</param>
/// <param name="auditRecordStore">The store used to read audit records.</param>
public sealed class DashboardService(
    IOrderRepository orderRepository,
    IPositionSnapshotRepository positionSnapshotRepository,
    IOperationalIssueStore issueStore,
    IAuditRecordStore auditRecordStore)
{
    private readonly IOrderRepository _orderRepository = orderRepository;
    private readonly IPositionSnapshotRepository _positionSnapshotRepository = positionSnapshotRepository;
    private readonly IOperationalIssueStore _issueStore = issueStore;
    private readonly IAuditRecordStore _auditRecordStore = auditRecordStore;

    public async Task<IReadOnlyList<DashboardOrderSummaryResponse>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Domain.Orders.Order> orders = await _orderRepository.GetOrdersAsync(cancellationToken);
        return orders
            .OrderByDescending(order => order.UpdatedAt)
            .Select(order => new DashboardOrderSummaryResponse(
                order.Id,
                order.TradeIntent.Ticker,
                order.TradeIntent.Side?.ToString().ToLowerInvariant(),
                order.TradeIntent.Quantity,
                order.TradeIntent.LimitPrice,
                order.TradeIntent.StrategyName,
                order.CurrentStatus.ToString().ToLowerInvariant(),
                order.PublishStatus.ToString().ToLowerInvariant(),
                order.LastResultStatus,
                order.TradeIntent.CorrelationId,
                order.TradeIntent.ActionType.ToString().ToLowerInvariant(),
                order.ExternalOrderId,
                order.FilledQuantity,
                order.UpdatedAt))
            .ToArray();
    }

    public async Task<IReadOnlyList<PositionResponse>> GetPositionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Domain.Positions.PositionSnapshot> positions = await _positionSnapshotRepository.GetPositionsAsync(cancellationToken);
        return positions
            .OrderBy(position => position.Ticker)
            .Select(position => new PositionResponse(
                position.Ticker,
                position.Side.ToString().ToLowerInvariant(),
                position.Contracts,
                position.AveragePrice,
                position.AsOf))
            .ToArray();
    }

    public async Task<IReadOnlyList<DashboardEventResponse>> GetEventsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Domain.Orders.Order> orders = await _orderRepository.GetOrdersAsync(cancellationToken);
        List<DashboardEventResponse> events = [];

        foreach (Domain.Orders.Order order in orders)
        {
            IReadOnlyList<Domain.Executions.ExecutionEvent> orderEvents = await _orderRepository.GetOrderEventsAsync(order.Id, cancellationToken);
            events.AddRange(orderEvents.Select(orderEvent => new DashboardEventResponse(
                order.Id,
                order.TradeIntent.Ticker,
                orderEvent.Status.ToString().ToLowerInvariant(),
                "order_status",
                null,
                orderEvent.FilledQuantity,
                orderEvent.OccurredAt)));

            IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)> lifecycleEvents = await _orderRepository.GetOrderLifecycleEventsAsync(order.Id, cancellationToken);
            events.AddRange(lifecycleEvents.Select(lifecycleEvent => new DashboardEventResponse(
                order.Id,
                order.TradeIntent.Ticker,
                lifecycleEvent.Stage,
                "lifecycle",
                lifecycleEvent.Details,
                order.FilledQuantity,
                lifecycleEvent.OccurredAt)));
        }

        return events
            .OrderByDescending(orderEvent => orderEvent.OccurredAt)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<DashboardIssueResponse>> GetIssuesAsync(string? category = null, int hours = 24, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Operations.OperationalIssue> issues = await _issueStore.GetRecentAsync(category, hours, cancellationToken);
        return issues
            .OrderByDescending(issue => issue.OccurredAt)
            .Select(issue => new DashboardIssueResponse(
                issue.Id,
                issue.Category,
                issue.Severity,
                issue.Source,
                issue.Message,
                issue.Details,
                issue.OccurredAt))
            .ToArray();
    }

    public async Task<IReadOnlyList<DashboardAuditRecordResponse>> GetAuditRecordsAsync(string? category = null, int hours = 24, int limit = 100, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Operations.AuditRecord> records = await _auditRecordStore.GetRecentAsync(category, hours, limit, cancellationToken);
        return records
            .Select(record => new DashboardAuditRecordResponse(
                record.Id,
                record.Category,
                record.Action,
                record.Outcome,
                record.CorrelationId,
                record.IdempotencyKey,
                record.ResourceId,
                record.Details,
                record.OccurredAt))
            .ToArray();
    }
}
