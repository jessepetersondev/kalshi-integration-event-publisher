using System.Text;
using System.Text.Json;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Contracts.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Publishes rabbit mq application event.
/// </summary>
public sealed class RabbitMqApplicationEventPublisher(
    IConnectionFactory connectionFactory,
    RabbitMqTopologyBootstrapper topologyBootstrapper,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqApplicationEventPublisher> logger) : IApplicationEventPublisher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionFactory _connectionFactory = connectionFactory;
    private readonly RabbitMqTopologyBootstrapper _topologyBootstrapper = topologyBootstrapper;
    private readonly ILogger<RabbitMqApplicationEventPublisher> _logger = logger;
    private readonly RabbitMqOptions _options = options.Value;

    public Task PublishAsync(ApplicationEventEnvelope applicationEvent, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string routingKey = BuildRoutingKey(applicationEvent);
        string payload = JsonSerializer.Serialize(applicationEvent, SerializerOptions);
        byte[] body = Encoding.UTF8.GetBytes(payload);

        Exception? lastException = null;
        int maxAttempts = Math.Max(1, _options.PublishRetryAttempts + 1);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using IConnection connection = _connectionFactory.CreateConnection(_options.ClientProvidedName);
                using IModel channel = connection.CreateModel();

                _topologyBootstrapper.EnsureTopology(channel);
                channel.ConfirmSelect();
                BasicReturnEventArgs? returnedMessage = null;
                channel.BasicReturn += (_, args) => returnedMessage = args;

                IBasicProperties properties = channel.CreateBasicProperties();
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
                    mandatory: true,
                    basicProperties: properties,
                    body: body);

                bool confirmed = channel.WaitForConfirms(
                    TimeSpan.FromMilliseconds(_options.PublishConfirmTimeoutMilliseconds),
                    out bool timedOut);

                if (timedOut)
                {
                    throw new PublishConfirmationException(
                        $"RabbitMQ did not confirm publication of event '{applicationEvent.Name}' with id '{applicationEvent.Id}'.",
                        RabbitMqPublishFailureKind.ConfirmTimeout,
                        isRetryable: true);
                }

                if (!confirmed)
                {
                    throw new PublishConfirmationException(
                        $"RabbitMQ negatively acknowledged publication of event '{applicationEvent.Name}' with id '{applicationEvent.Id}'.",
                        RabbitMqPublishFailureKind.Nack,
                        isRetryable: true);
                }

                if (returnedMessage is not null)
                {
                    throw new PublishConfirmationException(
                        $"RabbitMQ returned unroutable event '{applicationEvent.Name}' with id '{applicationEvent.Id}' for routing key '{returnedMessage.RoutingKey}'.",
                        RabbitMqPublishFailureKind.Unroutable,
                        isRetryable: true);
                }

                _logger.LogInformation(
                    "Published application event {EventName} to RabbitMQ exchange {Exchange} with routing key {RoutingKey}.",
                    applicationEvent.Name,
                    _options.Exchange,
                    routingKey);

                return Task.CompletedTask;
            }
            catch (Exception exception) when (attempt < maxAttempts && IsRetryable(exception))
            {
                lastException = exception;
                RecordPublishFailure(ClassifyFailure(exception));
                Thread.Sleep(TimeSpan.FromMilliseconds(_options.PublishRetryDelayMilliseconds * attempt));
            }
            catch (PublishConfirmationException exception)
            {
                RecordPublishFailure(exception.FailureKind);
                throw;
            }
            catch (Exception exception)
            {
                lastException = exception;
                RecordPublishFailure(ClassifyFailure(exception));
                break;
            }
        }

        throw lastException is PublishConfirmationException publishConfirmationException
            ? publishConfirmationException
            : new PublishConfirmationException(
                $"RabbitMQ publication could not be confirmed for event '{applicationEvent.Name}' with id '{applicationEvent.Id}'.",
                ClassifyFailure(lastException),
                IsRetryable(lastException ?? new InvalidOperationException("RabbitMQ publication failed without an inner exception.")),
                lastException);
    }

    private string BuildRoutingKey(ApplicationEventEnvelope applicationEvent)
    {
        string prefix = _options.RoutingKeyPrefix.Trim().Trim('.');
        string category = NormalizeSegment(applicationEvent.Category);
        string name = NormalizeSegment(applicationEvent.Name);

        return string.IsNullOrWhiteSpace(prefix)
            ? $"{category}.{name}"
            : $"{prefix}.{category}.{name}";
    }

    private static string NormalizeSegment(string value)
        => value.Trim().ToLowerInvariant().Replace('-', '.');

    private static Dictionary<string, object> BuildHeaders(ApplicationEventEnvelope applicationEvent)
    {
        Dictionary<string, object> headers = new()
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

        foreach (KeyValuePair<string, string?> attribute in applicationEvent.Attributes)
        {
            headers[$"attribute:{attribute.Key}"] = attribute.Value ?? string.Empty;
        }

        return headers;
    }

    private static bool IsRetryable(Exception exception)
        => exception switch
        {
            PublishConfirmationException publishFailure => publishFailure.IsRetryable,
            OperationInterruptedException interrupted when IsPermanentBrokerConfiguration(interrupted) => false,
            _ => exception is BrokerUnreachableException or OperationInterruptedException or AlreadyClosedException or IOException or TimeoutException,
        };

    private static RabbitMqPublishFailureKind ClassifyFailure(Exception? exception)
    {
        return exception switch
        {
            PublishConfirmationException publishFailure => publishFailure.FailureKind,
            BrokerUnreachableException => RabbitMqPublishFailureKind.BrokerUnavailable,
            AlreadyClosedException => RabbitMqPublishFailureKind.ChannelClosed,
            TimeoutException => RabbitMqPublishFailureKind.ConfirmTimeout,
            OperationInterruptedException interrupted when IsPermanentBrokerConfiguration(interrupted) => RabbitMqPublishFailureKind.Configuration,
            OperationInterruptedException => RabbitMqPublishFailureKind.ConnectionInterrupted,
            IOException => RabbitMqPublishFailureKind.ConnectionInterrupted,
            _ => RabbitMqPublishFailureKind.Unknown,
        };
    }

    private static bool IsPermanentBrokerConfiguration(OperationInterruptedException exception)
    {
        ushort replyCode = exception.ShutdownReason?.ReplyCode ?? 0;
        return replyCode is 403 or 404;
    }

    private void RecordPublishFailure(RabbitMqPublishFailureKind failureKind)
    {
        KalshiTelemetry.RabbitMqPublishFailuresTotal.Add(
            1,
            new KeyValuePair<string, object?>("component", _options.ClientProvidedName),
            new KeyValuePair<string, object?>("failure_kind", failureKind.ToString().ToLowerInvariant()));
    }
}
