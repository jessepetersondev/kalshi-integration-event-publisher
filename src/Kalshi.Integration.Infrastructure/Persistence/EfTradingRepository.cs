using System.Diagnostics;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;
using Kalshi.Integration.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kalshi.Integration.Infrastructure.Persistence;

/// <summary>
/// Provides persistence operations for ef trading.
/// </summary>


public sealed class EfTradingRepository : ITradeIntentRepository, IOrderRepository, IPositionSnapshotRepository
{
    private static readonly Action<ILogger, string, string, double, Exception?> DependencyCallSucceeded =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Information,
            new EventId(1100, nameof(DependencyCallSucceeded)),
            "Dependency call {Dependency} {Operation} succeeded in {ElapsedMs} ms.");

    private static readonly Action<ILogger, string, string, double, Exception?> DependencyCallFailed =
        LoggerMessage.Define<string, string, double>(
            LogLevel.Error,
            new EventId(1101, nameof(DependencyCallFailed)),
            "Dependency call {Dependency} {Operation} failed after {ElapsedMs} ms.");

    private readonly KalshiIntegrationDbContext _dbContext;
    private readonly ILogger<EfTradingRepository> _logger;

    public EfTradingRepository(KalshiIntegrationDbContext dbContext, ILogger<EfTradingRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task AddTradeIntentAsync(TradeIntent tradeIntent, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("trade-intents.insert", async () =>
        {
            var entity = new TradeIntentEntity
            {
                Id = tradeIntent.Id,
                Ticker = tradeIntent.Ticker,
                Side = tradeIntent.Side.ToString(),
                Quantity = tradeIntent.Quantity,
                LimitPrice = tradeIntent.LimitPrice,
                StrategyName = tradeIntent.StrategyName,
                CorrelationId = tradeIntent.CorrelationId,
                CreatedAt = tradeIntent.CreatedAt,
            };

            _dbContext.TradeIntents.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task<TradeIntent?> GetTradeIntentAsync(Guid tradeIntentId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("trade-intents.get-by-id", async () =>
        {
            var entity = await _dbContext.TradeIntents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tradeIntentId, cancellationToken);
            return entity is null ? null : MapTradeIntent(entity);
        });

    public Task<TradeIntent?> GetTradeIntentByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("trade-intents.get-by-correlation-id", async () =>
        {
            var entity = await _dbContext.TradeIntents.AsNoTracking().SingleOrDefaultAsync(x => x.CorrelationId == correlationId, cancellationToken);
            return entity is null ? null : MapTradeIntent(entity);
        });

    public Task AddOrderAsync(Order order, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("orders.insert", async () =>
        {
            var entity = new OrderEntity
            {
                Id = order.Id,
                TradeIntentId = order.TradeIntent.Id,
                Status = order.CurrentStatus.ToString(),
                FilledQuantity = order.FilledQuantity,
                CreatedAt = order.CreatedAt,
                UpdatedAt = order.UpdatedAt,
            };

            _dbContext.Orders.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task UpdateOrderAsync(Order order, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("orders.update", async () =>
        {
            var entity = await _dbContext.Orders.SingleAsync(x => x.Id == order.Id, cancellationToken);
            entity.Status = order.CurrentStatus.ToString();
            entity.FilledQuantity = order.FilledQuantity;
            entity.UpdatedAt = order.UpdatedAt;
            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task<Order?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("orders.get-by-id", async () =>
        {
            var orderEntity = await _dbContext.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
            if (orderEntity is null)
            {
                return null;
            }

            var tradeIntentEntity = await _dbContext.TradeIntents.AsNoTracking().SingleAsync(x => x.Id == orderEntity.TradeIntentId, cancellationToken);
            return MapOrder(orderEntity, tradeIntentEntity);
        });

    public Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync<IReadOnlyList<Order>>("orders.list", async () =>
        {
            var orderEntities = await _dbContext.Orders.AsNoTracking().ToListAsync(cancellationToken);
            if (orderEntities.Count == 0)
            {
                return Array.Empty<Order>();
            }

            var tradeIntentIds = orderEntities.Select(x => x.TradeIntentId).Distinct().ToArray();
            var tradeIntentEntities = await _dbContext.TradeIntents
                .AsNoTracking()
                .Where(x => tradeIntentIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

            return orderEntities
                .OrderByDescending(x => x.UpdatedAt)
                .Select(orderEntity => MapOrder(orderEntity, tradeIntentEntities[orderEntity.TradeIntentId]))
                .ToArray();
        });

    public Task AddOrderEventAsync(ExecutionEvent executionEvent, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("order-events.insert", async () =>
        {
            var entity = new OrderEventEntity
            {
                Id = executionEvent.Id,
                OrderId = executionEvent.OrderId,
                Status = executionEvent.Status.ToString(),
                FilledQuantity = executionEvent.FilledQuantity,
                OccurredAt = executionEvent.OccurredAt,
            };

            _dbContext.OrderEvents.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task<IReadOnlyList<ExecutionEvent>> GetOrderEventsAsync(Guid orderId, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync<IReadOnlyList<ExecutionEvent>>("order-events.list-by-order-id", async () =>
        {
            var entities = await _dbContext.OrderEvents.AsNoTracking().Where(x => x.OrderId == orderId).ToListAsync(cancellationToken);
            return entities.OrderBy(x => x.OccurredAt).Select(MapExecutionEvent).ToArray();
        });

    public Task UpsertPositionSnapshotAsync(PositionSnapshot positionSnapshot, CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync("position-snapshots.upsert", async () =>
        {
            var existing = await _dbContext.PositionSnapshots.SingleOrDefaultAsync(x => x.Ticker == positionSnapshot.Ticker && x.Side == positionSnapshot.Side.ToString(), cancellationToken);
            if (existing is null)
            {
                _dbContext.PositionSnapshots.Add(new PositionSnapshotEntity
                {
                    Id = Guid.NewGuid(),
                    Ticker = positionSnapshot.Ticker,
                    Side = positionSnapshot.Side.ToString(),
                    Contracts = positionSnapshot.Contracts,
                    AveragePrice = positionSnapshot.AveragePrice,
                    AsOf = positionSnapshot.AsOf,
                });
            }
            else
            {
                existing.Contracts = positionSnapshot.Contracts;
                existing.AveragePrice = positionSnapshot.AveragePrice;
                existing.AsOf = positionSnapshot.AsOf;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        });

    public Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken cancellationToken = default)
        => ExecuteDependencyCallAsync<IReadOnlyList<PositionSnapshot>>("position-snapshots.list", async () =>
        {
            var entities = await _dbContext.PositionSnapshots.AsNoTracking().OrderBy(x => x.Ticker).ToListAsync(cancellationToken);
            return entities.Select(MapPositionSnapshot).ToArray();
        });

    private async Task ExecuteDependencyCallAsync(string operation, Func<Task> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var dependencyName = GetDependencyName();

        try
        {
            await action();
            stopwatch.Stop();

            DependencyCallSucceeded(
                _logger,
                dependencyName,
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                null);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            DependencyCallFailed(
                _logger,
                dependencyName,
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                exception);

            throw;
        }
    }

    private async Task<T> ExecuteDependencyCallAsync<T>(string operation, Func<Task<T>> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var dependencyName = GetDependencyName();

        try
        {
            var result = await action();
            stopwatch.Stop();

            DependencyCallSucceeded(
                _logger,
                dependencyName,
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                null);

            return result;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            DependencyCallFailed(
                _logger,
                dependencyName,
                operation,
                stopwatch.Elapsed.TotalMilliseconds,
                exception);

            throw;
        }
    }

    private static TradeIntent MapTradeIntent(TradeIntentEntity entity)
    {
        return new TradeIntent(entity.Ticker, Enum.Parse<TradeSide>(entity.Side), entity.Quantity, entity.LimitPrice, entity.StrategyName, entity.CorrelationId, entity.CreatedAt)
            .WithId(entity.Id);
    }

    private static ExecutionEvent MapExecutionEvent(OrderEventEntity entity)
    {
        return new ExecutionEvent(entity.OrderId, Enum.Parse<OrderStatus>(entity.Status), entity.FilledQuantity, entity.OccurredAt)
            .WithId(entity.Id);
    }

    private static PositionSnapshot MapPositionSnapshot(PositionSnapshotEntity entity)
    {
        return new PositionSnapshot(entity.Ticker, Enum.Parse<TradeSide>(entity.Side), entity.Contracts, entity.AveragePrice, entity.AsOf);
    }

    private string GetDependencyName()
    {
        return DatabaseProviders.GetDependencyName(_dbContext.Database.ProviderName);
    }

    private static Order MapOrder(OrderEntity orderEntity, TradeIntentEntity tradeIntentEntity)
    {
        var tradeIntent = MapTradeIntent(tradeIntentEntity);
        var order = new Order(tradeIntent);
        order.SetPersistenceState(orderEntity.Id, Enum.Parse<OrderStatus>(orderEntity.Status), orderEntity.FilledQuantity, orderEntity.CreatedAt, orderEntity.UpdatedAt);
        return order;
    }
}
