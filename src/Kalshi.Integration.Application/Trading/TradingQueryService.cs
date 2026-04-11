using System.Text;

using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Contracts.Orders;
using Kalshi.Integration.Contracts.Positions;
using Kalshi.Integration.Domain.Orders;

namespace Kalshi.Integration.Application.Trading;

/// <summary>
/// Reads order and position projections without mutating trading state.
/// </summary>
public sealed class TradingQueryService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IPositionSnapshotRepository _positionSnapshotRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="TradingQueryService"/> class.
    /// </summary>
    /// <param name="orderRepository">The repository used to read orders.</param>
    /// <param name="positionSnapshotRepository">The repository used to read position snapshots.</param>
    public TradingQueryService(IOrderRepository orderRepository, IPositionSnapshotRepository positionSnapshotRepository)
    {
        _orderRepository = orderRepository;
        _positionSnapshotRepository = positionSnapshotRepository;
    }

    public async Task<OrderResponse?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.GetOrderAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        return await OrderResponseFactory.CreateAsync(order, _orderRepository, cancellationToken);
    }

    public async Task<IReadOnlyList<OrderOutcomeResponse>> GetOrderOutcomesAsync(
        Guid? orderId = null,
        string? correlationId = null,
        string? originService = null,
        string? status = null,
        string? publishStatus = null,
        string? outcomeState = null,
        string? resultStatus = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.GetOrdersAsync(cancellationToken);
        var filtered = orders.AsEnumerable();

        if (orderId.HasValue)
        {
            filtered = filtered.Where(order => order.Id == orderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            filtered = filtered.Where(order => string.Equals(order.TradeIntent.CorrelationId, correlationId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(originService))
        {
            filtered = filtered.Where(order => string.Equals(order.TradeIntent.OriginService, originService.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = NormalizeStatusToken(status);
            filtered = filtered.Where(order => NormalizeStatusToken(order.CurrentStatus.ToString()) == normalizedStatus);
        }

        if (!string.IsNullOrWhiteSpace(publishStatus))
        {
            var normalizedPublishStatus = NormalizeStatusToken(publishStatus);
            filtered = filtered.Where(order => NormalizeStatusToken(order.PublishStatus.ToString()) == normalizedPublishStatus);
        }

        if (!string.IsNullOrWhiteSpace(resultStatus))
        {
            var normalizedResultStatus = NormalizeStatusToken(resultStatus);
            filtered = filtered.Where(order => NormalizeStatusToken(order.LastResultStatus) == normalizedResultStatus);
        }

        if (!string.IsNullOrWhiteSpace(outcomeState))
        {
            var normalizedOutcomeState = NormalizeStatusToken(outcomeState);
            filtered = filtered.Where(order => NormalizeStatusToken(ResolveOutcomeState(order)) == normalizedOutcomeState);
        }

        return filtered
            .OrderByDescending(order => order.UpdatedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .Select(order => new OrderOutcomeResponse(
                order.Id,
                order.TradeIntent.Id,
                order.TradeIntent.Ticker,
                order.TradeIntent.Side?.ToString().ToLowerInvariant(),
                order.TradeIntent.Quantity,
                order.TradeIntent.LimitPrice,
                order.TradeIntent.StrategyName,
                order.TradeIntent.CorrelationId,
                order.TradeIntent.ActionType.ToString().ToLowerInvariant(),
                order.TradeIntent.OriginService,
                order.CurrentStatus.ToString().ToLowerInvariant(),
                order.PublishStatus.ToString().ToLowerInvariant(),
                ResolveOutcomeState(order),
                order.LastResultStatus,
                order.LastResultMessage,
                order.ExternalOrderId,
                order.FilledQuantity,
                order.CreatedAt,
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

    private static string ResolveOutcomeState(Order order)
    {
        var normalizedResultStatus = NormalizeStatusToken(order.LastResultStatus);
        if (!string.IsNullOrWhiteSpace(normalizedResultStatus))
        {
            if (normalizedResultStatus.Contains("deadletter", StringComparison.Ordinal)
                || normalizedResultStatus.Contains("failed", StringComparison.Ordinal)
                || normalizedResultStatus.Contains("blocked", StringComparison.Ordinal))
            {
                return "failed";
            }

            if (normalizedResultStatus.Contains("succeeded", StringComparison.Ordinal)
                || normalizedResultStatus.Contains("executed", StringComparison.Ordinal)
                || normalizedResultStatus.Contains("reconciled", StringComparison.Ordinal))
            {
                return "succeeded";
            }
        }

        return order.CurrentStatus switch
        {
            OrderStatus.Canceled => "canceled",
            OrderStatus.Rejected => "failed",
            OrderStatus.Settled => "settled",
            _ => order.PublishStatus switch
            {
                OrderPublishStatus.ManualInterventionRequired => "manual_intervention_required",
                OrderPublishStatus.RetryScheduled => "awaiting_publish_retry",
                OrderPublishStatus.PublishConfirmed => "awaiting_result",
                OrderPublishStatus.PublishAttempted => "awaiting_publish_confirmation",
                OrderPublishStatus.Accepted or OrderPublishStatus.OrderCreated => "pending_submission",
                _ => "unknown",
            },
        };
    }

    private static string NormalizeStatusToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
