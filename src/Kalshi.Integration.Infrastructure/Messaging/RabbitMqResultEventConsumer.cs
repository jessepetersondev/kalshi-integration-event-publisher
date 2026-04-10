using System.Text;
using System.Text.Json;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Operations;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Consumes executor result events from RabbitMQ and projects them into publisher-owned order lifecycle state.
/// </summary>
public sealed class RabbitMqResultEventConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionFactory _connectionFactory;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<RabbitMqResultEventConsumer> _logger;
    private readonly RabbitMqOptions _options;

    public RabbitMqResultEventConsumer(
        IConnectionFactory connectionFactory,
        IServiceScopeFactory serviceScopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqResultEventConsumer> logger)
    {
        _connectionFactory = connectionFactory;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task HandlePayloadAsync(string payload, CancellationToken cancellationToken = default)
    {
        ApplicationEventEnvelope envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<ApplicationEventEnvelope>(payload, SerializerOptions)
                ?? throw new DomainException("Result payload could not be deserialized.");
        }
        catch (Exception exception)
        {
            await RecordIssueAsync("result-consumer", $"Malformed result event: {exception.Message}", payload, cancellationToken);
            throw;
        }

        using var scope = _serviceScopeFactory.CreateScope();
        var tradingService = scope.ServiceProvider.GetRequiredService<TradingService>();
        var applied = await tradingService.ApplyExecutorResultAsync(envelope, cancellationToken);
        if (!applied)
        {
            _logger.LogInformation("Skipped duplicate executor result event {EventId}.", envelope.Id);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableResultConsumer)
        {
            return;
        }

        using var connection = _connectionFactory.CreateConnection($"{_options.ClientProvidedName}.results");
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(_options.Exchange, _options.ExchangeType, durable: true, autoDelete: false, arguments: null);
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
        channel.BasicQos(0, 1, false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.Received += async (_, args) =>
        {
            var payload = Encoding.UTF8.GetString(args.Body.ToArray());

            try
            {
                await HandlePayloadAsync(payload, stoppingToken);
                channel.BasicAck(args.DeliveryTag, multiple: false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to consume result event from queue {Queue}.", _options.PublisherResultsQueue);
                channel.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
            }
        };

        channel.BasicConsume(_options.PublisherResultsQueue, autoAck: false, consumer: consumer);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (TaskCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task RecordIssueAsync(string source, string message, string? details, CancellationToken cancellationToken)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var issueStore = scope.ServiceProvider.GetRequiredService<IOperationalIssueStore>();
        await issueStore.AddAsync(
            OperationalIssue.Create("integration", "error", source, message, details),
            cancellationToken);
    }
}
