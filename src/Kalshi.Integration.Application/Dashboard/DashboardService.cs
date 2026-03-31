using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.Dashboard;
using Kalshi.Integration.Contracts.Positions;

namespace Kalshi.Integration.Application.Dashboard;

/// <summary>
/// Coordinates dashboard operations.
/// </summary>


public sealed class DashboardService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPositionSnapshotRepository _positionSnapshotRepository;
    private readonly IOperationalIssueStore _issueStore;
    private readonly IAuditRecordStore _auditRecordStore;

    public DashboardService(
        IOrderRepository orderRepository,
        IPositionSnapshotRepository positionSnapshotRepository,
        IOperationalIssueStore issueStore,
        IAuditRecordStore auditRecordStore)
    {
        _orderRepository = orderRepository;
        _positionSnapshotRepository = positionSnapshotRepository;
        _issueStore = issueStore;
        _auditRecordStore = auditRecordStore;
    }

    public async Task<IReadOnlyList<DashboardOrderSummaryResponse>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetOrdersAsync(cancellationToken);
        return orders
            .OrderByDescending(order => order.UpdatedAt)
            .Select(order => new DashboardOrderSummaryResponse(
                order.Id,
                order.TradeIntent.Ticker,
                order.TradeIntent.Side.ToString().ToLowerInvariant(),
                order.TradeIntent.Quantity,
                order.TradeIntent.LimitPrice,
                order.TradeIntent.StrategyName,
                order.CurrentStatus.ToString().ToLowerInvariant(),
                order.FilledQuantity,
                order.UpdatedAt))
            .ToArray();
    }

    public async Task<IReadOnlyList<PositionResponse>> GetPositionsAsync(CancellationToken cancellationToken = default)
    {
        var positions = await _positionSnapshotRepository.GetPositionsAsync(cancellationToken);
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
        var orders = await _orderRepository.GetOrdersAsync(cancellationToken);
        var events = new List<DashboardEventResponse>();

        foreach (var order in orders)
        {
            var orderEvents = await _orderRepository.GetOrderEventsAsync(order.Id, cancellationToken);
            events.AddRange(orderEvents.Select(orderEvent => new DashboardEventResponse(
                order.Id,
                order.TradeIntent.Ticker,
                orderEvent.Status.ToString().ToLowerInvariant(),
                orderEvent.FilledQuantity,
                orderEvent.OccurredAt)));
        }

        return events
            .OrderByDescending(orderEvent => orderEvent.OccurredAt)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<DashboardIssueResponse>> GetIssuesAsync(string? category = null, int hours = 24, CancellationToken cancellationToken = default)
    {
        var issues = await _issueStore.GetRecentAsync(category, hours, cancellationToken);
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
        var records = await _auditRecordStore.GetRecentAsync(category, hours, limit, cancellationToken);
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
