using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kalshi.Integration.IntegrationTests;

public sealed class EfTradingRepositoryTests
{
    private static KalshiIntegrationDbContext CreateDbContext(string name)
    {
        DbContextOptions<KalshiIntegrationDbContext> options = new DbContextOptionsBuilder<KalshiIntegrationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new KalshiIntegrationDbContext(options);
    }

    private static KalshiIntegrationDbContext CreateSqliteDbContext(SqliteConnection connection)
    {
        DbContextOptions<KalshiIntegrationDbContext> options = new DbContextOptionsBuilder<KalshiIntegrationDbContext>()
            .UseSqlite(connection)
            .Options;

        KalshiIntegrationDbContext dbContext = new(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    [Fact]
    public async Task Repository_ShouldPersistTradeIntentAndOrderData()
    {
        await using KalshiIntegrationDbContext dbContext = CreateDbContext(Guid.NewGuid().ToString("N"));
        TestLogger<EfTradingRepository> logger = new();
        EfTradingRepository repository = new(dbContext, logger);

        TradeIntent tradeIntent = new("KXBTC-REPO", TradeSide.Yes, 2, 0.42m, "RepoTest");
        await repository.AddTradeIntentAsync(tradeIntent);

        Order order = new(tradeIntent);
        await repository.AddOrderAsync(order);
        await repository.AddOrderEventAsync(new ExecutionEvent(order.Id, order.CurrentStatus, order.FilledQuantity, order.CreatedAt));
        await repository.UpsertPositionSnapshotAsync(new PositionSnapshot(tradeIntent.Ticker, tradeIntent.Side, 0, tradeIntent.LimitPrice, DateTimeOffset.UtcNow));

        TradeIntent? storedIntent = await repository.GetTradeIntentAsync(tradeIntent.Id);
        Order? storedOrder = await repository.GetOrderAsync(order.Id);
        IReadOnlyList<ExecutionEvent> storedEvents = await repository.GetOrderEventsAsync(order.Id);
        IReadOnlyList<PositionSnapshot> positions = await repository.GetPositionsAsync();

        Assert.NotNull(storedIntent);
        Assert.NotNull(storedOrder);
        Assert.Single(storedEvents);
        Assert.Single(positions);
        Assert.Equal(tradeIntent.Ticker, storedIntent!.Ticker);
        Assert.Equal(order.Id, storedOrder!.Id);

        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Dependency call database trade-intents.insert succeeded", StringComparison.Ordinal));

        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Information
            && entry.Message.Contains("Dependency call database orders.get-by-id succeeded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindMatchingCancelTradeIntentAsync_ShouldReturnLatestMatch_WithSqlite()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using KalshiIntegrationDbContext dbContext = CreateSqliteDbContext(connection);
        EfTradingRepository repository = new(dbContext, new TestLogger<EfTradingRepository>());

        Guid targetPublisherOrderId = Guid.NewGuid();
        string targetClientOrderId = "client-order-123";
        TradeIntent older = new(
            "KXBTC-CANCEL-OLD",
            side: null,
            quantity: null,
            limitPrice: null,
            strategyName: "CancelOld",
            actionType: TradeIntentActionType.Cancel,
            originService: "kalshi-btc-quant",
            decisionReason: "older cancel",
            commandSchemaVersion: "kalshi-btc-quant.bridge.v1",
            targetPublisherOrderId: targetPublisherOrderId,
            targetClientOrderId: targetClientOrderId,
            correlationId: "cancel-old",
            createdAt: new DateTimeOffset(2026, 4, 10, 13, 30, 0, TimeSpan.Zero));
        TradeIntent newer = new(
            "KXBTC-CANCEL-NEW",
            side: null,
            quantity: null,
            limitPrice: null,
            strategyName: "CancelNew",
            actionType: TradeIntentActionType.Cancel,
            originService: "kalshi-btc-quant",
            decisionReason: "newer cancel",
            commandSchemaVersion: "kalshi-btc-quant.bridge.v1",
            targetPublisherOrderId: targetPublisherOrderId,
            targetClientOrderId: targetClientOrderId,
            correlationId: "cancel-new",
            createdAt: new DateTimeOffset(2026, 4, 10, 13, 31, 0, TimeSpan.Zero));

        await repository.AddTradeIntentAsync(older);
        await repository.AddTradeIntentAsync(newer);

        TradeIntent? result = await repository.FindMatchingCancelTradeIntentAsync(targetPublisherOrderId, targetClientOrderId, targetExternalOrderId: null);

        Assert.NotNull(result);
        Assert.Equal(newer.Id, result!.Id);
        Assert.Equal("cancel-new", result.CorrelationId);
    }
}
