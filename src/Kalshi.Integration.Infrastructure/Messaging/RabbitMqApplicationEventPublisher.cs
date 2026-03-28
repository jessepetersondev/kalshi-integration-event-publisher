using System.Text;
using System.Text.Json;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Kalshi.Integration.Infrastructure.Messaging;

public sealed class RabbitMqApplicationEventPublisher : IApplicationEventPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionFactory _connectionFactory;
    private readonly ILogger<RabbitMqApplicationEventPublisher> _logger;
    private readonly RabbitMqOptions _options;

    public RabbitMqApplicationEventPublisher(
        IConnectionFactory connectionFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqApplicationEventPublisher> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
        _options = options.Value;
    }

    public Task PublishAsync(ApplicationEventEnvelope applicationEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var routingKey = BuildRoutingKey(applicationEvent);
        var payload = JsonSerializer.Serialize(applicationEvent, SerializerOptions);
        var body = Encoding.UTF8.GetBytes(payload);

        using var connection = _connectionFactory.CreateConnection(_options.ClientProvidedName);
        using var channel = connection.CreateModel();

        channel.ExchangeDeclare(
            exchange: _options.Exchange,
            type: _options.ExchangeType,
            durable: true,
            autoDelete: false,
            arguments: null);

        var properties = channel.CreateBasicProperties();
        properties.AppId = _options.ClientProvidedName;
        properties.ContentType = "application/json";
        properties.DeliveryMode = 2;
        properties.MessageId = applicationEvent.Id.ToString();
        properties.CorrelationId = applicationEvent.CorrelationId ?? applicationEvent.Id.ToString();
        properties.Type = applicationEvent.Name;
        properties.Timestamp = new AmqpTimestamp(applicationEvent.OccurredAt.ToUnixTimeSeconds());
        properties.Headers = BuildHeaders(applicationEvent);

        channel.BasicPublish(
            exchange: _options.Exchange,
            routingKey: routingKey,
            mandatory: _options.Mandatory,
            basicProperties: properties,
            body: body);

        _logger.LogInformation(
            "Published application event {EventName} to RabbitMQ exchange {Exchange} with routing key {RoutingKey}.",
            applicationEvent.Name,
            _options.Exchange,
            routingKey);

        return Task.CompletedTask;
    }

    private string BuildRoutingKey(ApplicationEventEnvelope applicationEvent)
    {
        var prefix = _options.RoutingKeyPrefix.Trim().Trim('.');
        var category = NormalizeSegment(applicationEvent.Category);
        var name = NormalizeSegment(applicationEvent.Name);

        return string.IsNullOrWhiteSpace(prefix)
            ? $"{category}.{name}"
            : $"{prefix}.{category}.{name}";
    }

    private static string NormalizeSegment(string value)
        => value.Trim().ToLowerInvariant().Replace('-', '.');

    private static Dictionary<string, object> BuildHeaders(ApplicationEventEnvelope applicationEvent)
    {
        var headers = new Dictionary<string, object>
        {
            ["event-id"] = applicationEvent.Id.ToString(),
            ["category"] = applicationEvent.Category,
            ["event-name"] = applicationEvent.Name,
            ["occurred-at"] = applicationEvent.OccurredAt.ToString("O"),
        };

        if (!string.IsNullOrWhiteSpace(applicationEvent.ResourceId))
        {
            headers["resource-id"] = applicationEvent.ResourceId;
        }

        if (!string.IsNullOrWhiteSpace(applicationEvent.CorrelationId))
        {
            headers["correlation-id"] = applicationEvent.CorrelationId;
        }

        if (!string.IsNullOrWhiteSpace(applicationEvent.IdempotencyKey))
        {
            headers["idempotency-key"] = applicationEvent.IdempotencyKey;
        }

        foreach (var attribute in applicationEvent.Attributes)
        {
            headers[$"attribute:{attribute.Key}"] = attribute.Value ?? string.Empty;
        }

        return headers;
    }
}
