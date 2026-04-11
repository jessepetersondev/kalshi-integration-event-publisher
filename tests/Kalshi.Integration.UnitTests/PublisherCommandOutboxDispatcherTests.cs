using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.TradeIntents;
using Kalshi.Integration.Infrastructure.Messaging;
using Kalshi.Integration.Infrastructure.Operations;
using Kalshi.Integration.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kalshi.Integration.UnitTests;

public sealed class PublisherCommandOutboxDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_ShouldRetryDurablyAndEventuallyConfirmPublication()
    {
        var repository = new InMemoryTradingRepository();
        var submissionService = new OrderSubmissionService(repository, repository, repository);
        var publisher = new FlakyApplicationEventPublisher(failuresBeforeSuccess: 1);
        var dispatcher = new PublisherCommandOutboxDispatcher(
            repository,
            publisher,
            new InMemoryOperationalIssueStore(),
            Options.Create(new RabbitMqOptions
            {
                OutboxInitialRetryDelayMilliseconds = 1,
                OutboxJitterMaxMilliseconds = 0,
                OutboxMaxAttempts = 3,
                OutboxBatchSize = 5,
            }),
            NullLogger<PublisherCommandOutboxDispatcher>.Instance);

        var tradeIntent = new TradeIntent("KXBTC", TradeSide.Yes, 2, 0.45m, "Breakout", "corr-outbox");
        await repository.AddTradeIntentAsync(tradeIntent);

        var created = await submissionService.SubmitOrderAsync(
            new Contracts.Orders.CreateOrderRequest(tradeIntent.Id),
            "corr-outbox",
            "corr-outbox");

        Assert.NotNull(created.CommandEventId);

        await dispatcher.DispatchAsync(created.CommandEventId!.Value);
        var afterFailure = await repository.GetOrderAsync(created.Id);
        Assert.NotNull(afterFailure);
        Assert.Equal(OrderPublishStatus.RetryScheduled, afterFailure!.PublishStatus);
        Assert.Equal(0, publisher.SuccessfulPublishCount);

        await Task.Delay(5);
        await dispatcher.DrainDueMessagesAsync();

        var afterSuccess = await repository.GetOrderAsync(created.Id);
        Assert.NotNull(afterSuccess);
        Assert.Equal(OrderPublishStatus.PublishConfirmed, afterSuccess!.PublishStatus);
        Assert.Equal(1, publisher.SuccessfulPublishCount);
    }

    private sealed class FlakyApplicationEventPublisher : IApplicationEventPublisher
    {
        private int _remainingFailures;

        public FlakyApplicationEventPublisher(int failuresBeforeSuccess)
        {
            _remainingFailures = failuresBeforeSuccess;
        }

        public int SuccessfulPublishCount { get; private set; }

        public Task PublishAsync(ApplicationEventEnvelope applicationEvent, CancellationToken cancellationToken = default)
        {
            if (_remainingFailures-- > 0)
            {
                throw new PublishConfirmationException("simulated broker interruption", RabbitMqPublishFailureKind.ConnectionInterrupted, isRetryable: true);
            }

            SuccessfulPublishCount++;
            return Task.CompletedTask;
        }
    }
}
