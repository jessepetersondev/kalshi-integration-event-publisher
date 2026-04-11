using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Infrastructure.Operations;

namespace Kalshi.Integration.UnitTests;

public sealed class InMemoryApplicationEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_ShouldDispatchEventToInProcessSubscribersAndRetainHistory()
    {
        InMemoryApplicationEventPublisher publisher = new();
        List<ApplicationEventEnvelope> received = [];

        using IDisposable subscription = publisher.Subscribe((applicationEvent, cancellationToken) =>
        {
            received.Add(applicationEvent);
            return Task.CompletedTask;
        });

        ApplicationEventEnvelope applicationEvent = ApplicationEventEnvelope.Create(
            category: "trading",
            name: "order.created",
            resourceId: Guid.NewGuid().ToString(),
            correlationId: "corr-1",
            idempotencyKey: "idem-1",
            attributes: new Dictionary<string, string?>
            {
                ["ticker"] = "KXBTC-PUB",
                ["status"] = "pending",
            });

        await publisher.PublishAsync(applicationEvent);

        IReadOnlyList<ApplicationEventEnvelope> publishedEvents = publisher.GetPublishedEvents();
        Assert.Single(received);
        Assert.Single(publishedEvents);
        Assert.Equal(applicationEvent, received[0]);
        Assert.Equal(applicationEvent, publishedEvents[0]);
    }
}
