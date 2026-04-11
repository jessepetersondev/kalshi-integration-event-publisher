using System.Text;
using System.Text.Json;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Infrastructure.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;

namespace Kalshi.Integration.UnitTests;

public sealed class RabbitMqApplicationEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_ShouldDeclareExchangeAndPublishSerializedEvent()
    {
        IOptions<RabbitMqOptions> options = Options.Create(new RabbitMqOptions
        {
            Exchange = "kalshi.events",
            ExchangeType = "topic",
            RoutingKeyPrefix = "kalshi.integration",
            ClientProvidedName = "kalshi-unit-tests",
        });

        Mock<IConnectionFactory> connectionFactory = new(MockBehavior.Strict);
        Mock<IConnection> connection = new(MockBehavior.Strict);
        Mock<IModel> model = new(MockBehavior.Strict);
        Mock<IBasicProperties> properties = new(MockBehavior.Strict);
        properties.SetupAllProperties();
        properties.Object.Headers = new Dictionary<string, object>();

        byte[]? publishedBody = null;
        string? routingKey = null;

        connectionFactory
            .Setup(x => x.CreateConnection("kalshi-unit-tests"))
            .Returns(connection.Object);
        connection
            .Setup(x => x.CreateModel())
            .Returns(model.Object);
        model
            .Setup(x => x.ExchangeDeclare("kalshi.events", "topic", true, false, null));
        model
            .Setup(x => x.QueueDeclare("kalshi.integration.executor.dlq", true, false, false, null))
            .Returns((QueueDeclareOk)null!);
        model
            .Setup(x => x.QueueDeclare(
                "kalshi.integration.executor",
                true,
                false,
                false,
                It.Is<Dictionary<string, object>>(args =>
                    (string)args["x-dead-letter-exchange"] == string.Empty &&
                    (string)args["x-dead-letter-routing-key"] == "kalshi.integration.executor.dlq")))
            .Returns((QueueDeclareOk)null!);
        model
            .Setup(x => x.QueueBind("kalshi.integration.executor", "kalshi.events", "kalshi.integration.trading.#", null));
        model
            .Setup(x => x.QueueDeclare("kalshi.integration.event-publisher.results.dlq", true, false, false, null))
            .Returns((QueueDeclareOk)null!);
        model
            .Setup(x => x.QueueDeclare(
                "kalshi.integration.event-publisher.results",
                true,
                false,
                false,
                It.Is<Dictionary<string, object>>(args =>
                    (string)args["x-dead-letter-exchange"] == string.Empty &&
                    (string)args["x-dead-letter-routing-key"] == "kalshi.integration.event-publisher.results.dlq")))
            .Returns((QueueDeclareOk)null!);
        model
            .Setup(x => x.QueueBind("kalshi.integration.event-publisher.results", "kalshi.events", "kalshi.integration.results.#", null));
        model
            .Setup(x => x.ConfirmSelect());
        model
            .Setup(x => x.CreateBasicProperties())
            .Returns(properties.Object);
        model
            .Setup(x => x.BasicPublish(
                "kalshi.events",
                It.IsAny<string>(),
                true,
                properties.Object,
                It.IsAny<ReadOnlyMemory<byte>>()))
            .Callback((string _, string key, bool _, IBasicProperties _, ReadOnlyMemory<byte> body) =>
            {
                routingKey = key;
                publishedBody = body.ToArray();
            });
        bool confirmTimedOut = false;
        model
            .Setup(x => x.WaitForConfirms(It.IsAny<TimeSpan>(), out confirmTimedOut))
            .Returns(true);
        model.Setup(x => x.Dispose());
        connection.Setup(x => x.Dispose());

        RabbitMqTopologyBootstrapper topologyBootstrapper = new(options);
        RabbitMqApplicationEventPublisher publisher = new(
            connectionFactory.Object,
            topologyBootstrapper,
            options,
            NullLogger<RabbitMqApplicationEventPublisher>.Instance);
        ApplicationEventEnvelope applicationEvent = ApplicationEventEnvelope.Create(
            category: "trading",
            name: "order.created",
            resourceId: Guid.NewGuid().ToString(),
            correlationId: "corr-1",
            idempotencyKey: "idem-1",
            attributes: new Dictionary<string, string?>
            {
                ["ticker"] = "KXBTC",
                ["status"] = "pending",
            },
            occurredAt: new DateTimeOffset(2026, 3, 28, 20, 0, 0, TimeSpan.Zero));

        await publisher.PublishAsync(applicationEvent);

        Assert.Equal("kalshi.integration.trading.order.created", routingKey);
        Assert.NotNull(publishedBody);
        using JsonDocument json = JsonDocument.Parse(Encoding.UTF8.GetString(publishedBody!));
        Assert.Equal("trading", json.RootElement.GetProperty("category").GetString());
        Assert.Equal("order.created", json.RootElement.GetProperty("name").GetString());
        Assert.Equal("corr-1", json.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("idem-1", json.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.Equal("application/json", properties.Object.ContentType);
        Assert.Equal("order.created", properties.Object.Type);
        Assert.Equal("corr-1", properties.Object.CorrelationId);
        Assert.Equal("idem-1", properties.Object.Headers!["idempotency-key"]);
        Assert.Equal("corr-1", properties.Object.Headers["correlation-id"]);
        Assert.Equal("KXBTC", properties.Object.Headers["attribute:ticker"]);

        connectionFactory.VerifyAll();
        connection.VerifyAll();
        model.VerifyAll();
    }
}
