using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kalshi.Integration.IntegrationTests;

public sealed class EfTradingRepositoryTests
{
    private static KalshiIntegrationDbContext CreateDbContext(string name)
    {
        var options = new DbContextOptionsBuilder<KalshiIntegrationDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new KalshiIntegrationDbContext(options);
    }

    [Fact]
    public async Task Repository_ShouldPersistTradeIntentAndOrderData()
    {
        await using var dbContext = CreateDbContext(Guid.NewGuid().ToString("N"));
        var logger = new TestLogger<EfTradingRepository>();
        var repository = new EfTradingRepository(dbContext, logger);

        var tradeIntent = new TradeIntent("KXBTC-REPO", TradeSide.Yes, 2, 0.42m, "RepoTest");
        await repository.AddTradeIntentAsync(tradeIntent);

        var order = new Order(tradeIntent);
        await repository.AddOrderAsync(order);
        await repository.AddOrderEventAsync(new ExecutionEvent(order.Id, order.CurrentStatus, order.FilledQuantity, order.CreatedAt));
        await repository.UpsertPositionSnapshotAsync(new PositionSnapshot(tradeIntent.Ticker, tradeIntent.Side, 0, tradeIntent.LimitPrice, DateTimeOffset.UtcNow));

        var storedIntent = await repository.GetTradeIntentAsync(tradeIntent.Id);
        var storedOrder = await repository.GetOrderAsync(order.Id);
        var storedEvents = await repository.GetOrderEventsAsync(order.Id);
        var positions = await repository.GetPositionsAsync();

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
}
