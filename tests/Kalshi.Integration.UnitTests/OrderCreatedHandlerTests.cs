using System.Text.Json.Nodes;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Executor.Execution;
using Kalshi.Integration.Executor.Handlers;
using Kalshi.Integration.Executor.Messaging;
using Kalshi.Integration.Executor.Persistence;
using Kalshi.Integration.Infrastructure.Integrations.Kalshi;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Kalshi.Integration.UnitTests;

public sealed class OrderCreatedHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldRecoverExistingExternalOrderByClientOrderIdWithoutPlacingDuplicateOrder()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateDbContext(connection);

        var apiClient = new Mock<IKalshiApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(x => x.GetOrdersAsync("KXBTC", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonObject
            {
                ["orders"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["order_id"] = "ext-123",
                        ["client_order_id"] = "client-order-123",
                        ["status"] = "resting",
                        ["fill_count_fp"] = "0",
                    },
                },
            });

        var handler = new OrderCreatedHandler(
            dbContext,
            apiClient.Object,
            new RabbitMqResultEventPublisher(),
            new RabbitMqInboundEventPublisher(),
            new DeadLetterEventPublisher(),
            new ExecutionReliabilityPolicy(),
            Options.Create(new KalshiApiOptions
            {
                BaseUrl = "https://example.test",
                Subaccount = 7,
                UserAgent = "executor-tests",
            }),
            NullLogger<OrderCreatedHandler>.Instance);

        var envelope = new ApplicationEventEnvelope(
            Guid.NewGuid(),
            "trading",
            "order.created",
            Guid.NewGuid().ToString(),
            "corr-123",
            "corr-123",
            new Dictionary<string, string?>
            {
                ["publisherOrderId"] = Guid.NewGuid().ToString(),
                ["tradeIntentId"] = Guid.NewGuid().ToString(),
                ["ticker"] = "KXBTC",
                ["actionType"] = "entry",
                ["side"] = "yes",
                ["quantity"] = "2",
                ["limitPrice"] = "0.45",
                ["clientOrderId"] = "client-order-123",
            },
            DateTimeOffset.UtcNow);

        await handler.HandleAsync(envelope);

        var execution = await dbContext.ExecutionRecords.SingleAsync();
        var outboxMessage = await dbContext.OutboxMessages.SingleAsync(x => x.MessageType == "result");

        Assert.Equal("ext-123", execution.ExternalOrderId);
        Assert.Equal("resting", execution.Status);
        Assert.Equal(outboxMessage.ExecutionRecordId, execution.Id);
        apiClient.Verify(x => x.PlaceOrderAsync(It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_ShouldIgnoreReplayOfSameInboundCommandAfterSuccessfulPlacement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = CreateDbContext(connection);

        var apiClient = new Mock<IKalshiApiClient>(MockBehavior.Strict);
        apiClient
            .Setup(x => x.GetOrdersAsync("KXBTC", 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new JsonObject { ["orders"] = new JsonArray() });
        apiClient
            .Setup(x => x.PlaceOrderAsync(It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JsonObject payload, CancellationToken _) => new JsonObject
            {
                ["order"] = new JsonObject
                {
                    ["order_id"] = "ext-replay-123",
                    ["client_order_id"] = payload["client_order_id"]?.GetValue<string>(),
                    ["status"] = "resting",
                    ["fill_count_fp"] = "0",
                },
            });

        var handler = new OrderCreatedHandler(
            dbContext,
            apiClient.Object,
            new RabbitMqResultEventPublisher(),
            new RabbitMqInboundEventPublisher(),
            new DeadLetterEventPublisher(),
            new ExecutionReliabilityPolicy(),
            Options.Create(new KalshiApiOptions
            {
                BaseUrl = "https://example.test",
                Subaccount = 7,
                UserAgent = "executor-tests",
            }),
            NullLogger<OrderCreatedHandler>.Instance);

        var publisherOrderId = Guid.NewGuid();
        var tradeIntentId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var envelope = new ApplicationEventEnvelope(
            eventId,
            "trading",
            "order.created",
            publisherOrderId.ToString(),
            "corr-replay",
            "corr-replay",
            new Dictionary<string, string?>
            {
                ["publisherOrderId"] = publisherOrderId.ToString(),
                ["tradeIntentId"] = tradeIntentId.ToString(),
                ["ticker"] = "KXBTC",
                ["actionType"] = "entry",
                ["side"] = "yes",
                ["quantity"] = "2",
                ["limitPrice"] = "0.45",
                ["clientOrderId"] = "client-order-replay-123",
            },
            DateTimeOffset.UtcNow);

        await handler.HandleAsync(envelope);
        await handler.HandleAsync(envelope);

        Assert.Equal(1, await dbContext.ExecutionRecords.CountAsync());
        Assert.Equal(1, await dbContext.ExternalOrderMappings.CountAsync());
        Assert.Equal(2, await dbContext.OutboxMessages.CountAsync());

        apiClient.Verify(x => x.PlaceOrderAsync(It.IsAny<JsonObject>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static ExecutorDbContext CreateDbContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<ExecutorDbContext>()
            .UseSqlite(connection)
            .Options;

        var dbContext = new ExecutorDbContext(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }
}
