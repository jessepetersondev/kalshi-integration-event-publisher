using RabbitMQ.Client;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Declares the shared publisher and executor RabbitMQ topology so messages always
/// have durable queues bound before they are published or consumed.
/// </summary>
public sealed class RabbitMqTopologyBootstrapper(Microsoft.Extensions.Options.IOptions<RabbitMqOptions> options)
{
    private readonly RabbitMqOptions _options = options.Value;

    public void EnsureTopology(IModel channel)
    {
        channel.ExchangeDeclare(_options.Exchange, _options.ExchangeType, durable: true, autoDelete: false, arguments: null);

        channel.QueueDeclare(
            queue: _options.ExecutorDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        channel.QueueDeclare(
            queue: _options.ExecutorQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = _options.ExecutorDeadLetterQueue,
            });
        channel.QueueBind(_options.ExecutorQueue, _options.Exchange, _options.ExecutorRoutingKeyBinding);

        channel.QueueDeclare(
            queue: _options.PublisherResultsDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);
        channel.QueueDeclare(
            queue: _options.PublisherResultsQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object>
            {
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = _options.PublisherResultsDeadLetterQueue,
            });
        channel.QueueBind(_options.PublisherResultsQueue, _options.Exchange, _options.ResultsRoutingKeyBinding);
    }
}
