using System.Text;
using System.Text.Json;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Contracts.Diagnostics;
using Kalshi.Integration.Executor.Handlers;
using Kalshi.Integration.Executor.Persistence;
using Kalshi.Integration.Infrastructure.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Kalshi.Integration.Executor.Messaging;

public sealed class ExecutorInboundConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionFactory _connectionFactory;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly RabbitMqTopologyBootstrapper _topologyBootstrapper;
    private readonly IOptions<RabbitMqOptions> _options;
    private readonly ILogger<ExecutorInboundConsumer> _logger;

    public ExecutorInboundConsumer(
        IConnectionFactory connectionFactory,
        IServiceScopeFactory serviceScopeFactory,
        RabbitMqTopologyBootstrapper topologyBootstrapper,
        IOptions<RabbitMqOptions> options,
        ILogger<ExecutorInboundConsumer> logger)
    {
        _connectionFactory = connectionFactory;
        _serviceScopeFactory = serviceScopeFactory;
        _topologyBootstrapper = topologyBootstrapper;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var connection = _connectionFactory.CreateConnection($"{_options.Value.ClientProvidedName}.consumer");
                using var channel = connection.CreateModel();

                _topologyBootstrapper.EnsureTopology(channel);
                channel.BasicQos(0, 1, false);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.Received += async (_, args) =>
                {
                    var payload = Encoding.UTF8.GetString(args.Body.ToArray());

                    try
                    {
                        var envelope = JsonSerializer.Deserialize<ApplicationEventEnvelope>(payload, SerializerOptions)
                            ?? throw new InvalidOperationException("Inbound executor payload could not be deserialized.");

                        using var scope = _serviceScopeFactory.CreateScope();
                        if (string.Equals(envelope.Name, "order.created", StringComparison.OrdinalIgnoreCase))
                        {
                            var handler = scope.ServiceProvider.GetRequiredService<OrderCreatedHandler>();
                            await handler.HandleAsync(envelope, stoppingToken);
                            channel.BasicAck(args.DeliveryTag, false);
                            return;
                        }

                        _logger.LogWarning("Executor received unsupported event {EventName}; acknowledging without processing.", envelope.Name);
                        channel.BasicAck(args.DeliveryTag, false);
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(exception, "Executor failed to process inbound delivery {DeliveryTag}.", args.DeliveryTag);
                        channel.BasicNack(args.DeliveryTag, false, requeue: true);
                    }
                };

                channel.BasicConsume(_options.Value.ExecutorQueue, autoAck: false, consumer);
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                KalshiTelemetry.RabbitMqReconnectFailuresTotal.Add(
                    1,
                    new KeyValuePair<string, object?>("component", "executor"));

                using var scope = _serviceScopeFactory.CreateScope();
                var issueRecorder = scope.ServiceProvider.GetRequiredService<ExecutorOperationalIssueRecorder>();
                await issueRecorder.AddAsync(
                    "reliability",
                    "error",
                    "executor-consumer",
                    $"Executor inbound consumer failed to connect to RabbitMQ queue '{_options.Value.ExecutorQueue}'.",
                    exception.Message,
                    stoppingToken);
                _logger.LogError(exception, "Executor inbound consumer connection failed.");
                await Task.Delay(TimeSpan.FromSeconds(_options.Value.ConsumerRecoveryDelaySeconds), stoppingToken);
            }
        }
    }
}
