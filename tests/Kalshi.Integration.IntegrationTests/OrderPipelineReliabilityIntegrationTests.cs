using System.Text.Json.Nodes;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Risk;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Executor.Execution;
using Kalshi.Integration.Executor.Handlers;
using Kalshi.Integration.Executor.Messaging;
using Kalshi.Integration.Executor.Persistence;
using Kalshi.Integration.Infrastructure.Integrations.Kalshi;
using Kalshi.Integration.Infrastructure.Messaging;
using Kalshi.Integration.Infrastructure.Operations;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.IntegrationTests;

public sealed class OrderPipelineReliabilityIntegrationTests
{
    [Fact]
    public async Task OrderPipeline_ShouldEventuallySucceedExactlyOnceAcrossTransientPublishFailures()
    {
        await using SqliteConnection publisherConnection = new("Data Source=:memory:");
        await publisherConnection.OpenAsync();
        await using SqliteConnection executorConnection = new("Data Source=:memory:");
        await executorConnection.OpenAsync();

        await using KalshiIntegrationDbContext publisherDbContext = CreatePublisherDbContext(publisherConnection);
        await using ExecutorDbContext executorDbContext = CreateExecutorDbContext(executorConnection);

        EfTradingRepository publisherRepository = new(publisherDbContext, new TestLogger<EfTradingRepository>());
        RiskEvaluator riskEvaluator = new(publisherRepository, Options.Create(new RiskOptions { MaxOrderSize = 10 }));
        TradingService tradingService = new(publisherRepository, publisherRepository, publisherRepository, publisherRepository, riskEvaluator);
        OrderSubmissionService orderSubmissionService = new(publisherRepository, publisherRepository, publisherRepository);
        TradingQueryService queryService = new(publisherRepository, publisherRepository);

        FlakyPublisher commandPublisher = new(failuresBeforeSuccess: 1);
        PublisherCommandOutboxDispatcher publisherDispatcher = new(
            publisherRepository,
            commandPublisher,
            new InMemoryOperationalIssueStore(),
            Options.Create(new RabbitMqOptions
            {
                OutboxInitialRetryDelayMilliseconds = 1,
                OutboxJitterMaxMilliseconds = 0,
                OutboxMaxAttempts = 3,
                OutboxBatchSize = 10,
            }),
            NullLogger<PublisherCommandOutboxDispatcher>.Instance);

        Domain.TradeIntents.TradeIntent tradeIntent = new("KXBTC", Domain.TradeIntents.TradeSide.Yes, 2, 0.45m, "Breakout", "corr-e2e");
        await publisherRepository.AddTradeIntentAsync(tradeIntent);

        Contracts.Orders.OrderResponse created = await orderSubmissionService.SubmitOrderAsync(new Contracts.Orders.CreateOrderRequest(tradeIntent.Id), "corr-e2e", "corr-e2e");
        await publisherDispatcher.DispatchAsync(created.CommandEventId!.Value);
        await Task.Delay(5);
        await publisherDispatcher.DrainDueMessagesAsync();
        ApplicationEventEnvelope commandEnvelope = Assert.Single(commandPublisher.Events);

        FakeKalshiApiClient kalshiClient = new();
        OrderCreatedHandler handler = new(
            executorDbContext,
            kalshiClient,
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

        await handler.HandleAsync(commandEnvelope);

        FlakyPublisher resultPublisher = new(failuresBeforeSuccess: 1);
        ExecutorOutboxDispatcher executorOutboxDispatcher = new(
            executorDbContext,
            resultPublisher,
            new ExecutorOperationalIssueRecorder(executorDbContext),
            Options.Create(new RabbitMqOptions
            {
                OutboxInitialRetryDelayMilliseconds = 1,
                OutboxJitterMaxMilliseconds = 0,
                OutboxMaxAttempts = 3,
                OutboxBatchSize = 10,
            }),
            NullLogger<ExecutorOutboxDispatcher>.Instance);

        await executorOutboxDispatcher.DrainDueMessagesAsync();
        await Task.Delay(5);
        await executorOutboxDispatcher.DrainDueMessagesAsync();

        ApplicationEventEnvelope resultEnvelope = Assert.Single(resultPublisher.Events.Where(x => x.Name == "order.execution_succeeded"));
        await tradingService.ApplyExecutorResultAsync(resultEnvelope);

        Contracts.Orders.OrderResponse? order = await queryService.GetOrderAsync(created.Id);
        Assert.NotNull(order);
        Assert.Equal("resting", order!.Status);
        Assert.Equal("publishconfirmed", order.PublishStatus);
        Assert.Equal(1, kalshiClient.PlaceOrderCallCount);
        Assert.Single(resultPublisher.Events.Where(x => x.Name == "order.execution_succeeded"));
    }

    private static KalshiIntegrationDbContext CreatePublisherDbContext(SqliteConnection connection)
    {
        DbContextOptions<KalshiIntegrationDbContext> options = new DbContextOptionsBuilder<KalshiIntegrationDbContext>()
            .UseSqlite(connection)
            .Options;

        KalshiIntegrationDbContext dbContext = new(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private static ExecutorDbContext CreateExecutorDbContext(SqliteConnection connection)
    {
        DbContextOptions<ExecutorDbContext> options = new DbContextOptionsBuilder<ExecutorDbContext>()
            .UseSqlite(connection)
            .Options;

        ExecutorDbContext dbContext = new(options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private sealed class FlakyPublisher(int failuresBeforeSuccess) : IApplicationEventPublisher
    {
        private int _remainingFailures = failuresBeforeSuccess;

        public List<ApplicationEventEnvelope> Events { get; } = [];

        public Task PublishAsync(ApplicationEventEnvelope applicationEvent, CancellationToken cancellationToken = default)
        {
            if (_remainingFailures-- > 0)
            {
                throw new PublishConfirmationException("simulated broker interruption", RabbitMqPublishFailureKind.ConnectionInterrupted, isRetryable: true);
            }

            Events.Add(applicationEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeKalshiApiClient : IKalshiApiClient
    {
        public int PlaceOrderCallCount { get; private set; }

        public Task<JsonNode> GetBalanceAsync(int subaccount, CancellationToken cancellationToken = default) => Task.FromResult<JsonNode>(new JsonObject());
        public Task<JsonNode> GetPositionsAsync(int subaccount, CancellationToken cancellationToken = default) => Task.FromResult<JsonNode>(new JsonObject());
        public Task<JsonNode> GetSeriesAsync(string? category, IReadOnlyList<string> tags, CancellationToken cancellationToken = default) => Task.FromResult<JsonNode>(new JsonObject());
        public Task<JsonNode> GetMarketsAsync(string? status, int limit, string? seriesTicker, string? cursor, CancellationToken cancellationToken = default) => Task.FromResult<JsonNode>(new JsonObject());
        public Task<JsonNode> GetMarketAsync(string ticker, CancellationToken cancellationToken = default) => Task.FromResult<JsonNode>(new JsonObject());

        public Task<JsonNode> PlaceOrderAsync(JsonObject payload, CancellationToken cancellationToken = default)
        {
            PlaceOrderCallCount++;
            return Task.FromResult<JsonNode>(new JsonObject
            {
                ["order"] = new JsonObject
                {
                    ["order_id"] = "ext-e2e-1",
                    ["client_order_id"] = payload["client_order_id"]?.GetValue<string>(),
                    ["status"] = "resting",
                    ["fill_count_fp"] = "0",
                },
            });
        }

        public Task<JsonNode> GetOrderAsync(string externalOrderId, int subaccount, CancellationToken cancellationToken = default)
            => Task.FromResult<JsonNode>(new JsonObject());

        public Task<JsonNode> GetOrdersAsync(string? ticker, int subaccount, CancellationToken cancellationToken = default)
            => Task.FromResult<JsonNode>(new JsonObject { ["orders"] = new JsonArray() });

        public Task<JsonNode> CancelOrderAsync(string externalOrderId, int subaccount, CancellationToken cancellationToken = default)
            => Task.FromResult<JsonNode>(new JsonObject());
    }
}
