using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Samples RabbitMQ queue state and derives backlog-age and growth signals over time.
/// </summary>
public sealed class RabbitMqQueueInspector
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly RabbitMqTopologyBootstrapper _topologyBootstrapper;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqQueueInspector> _logger;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, QueueObservationState> _queueStates = new(StringComparer.Ordinal);

    public RabbitMqQueueInspector(
        IConnectionFactory connectionFactory,
        RabbitMqTopologyBootstrapper topologyBootstrapper,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqQueueInspector> logger)
    {
        _connectionFactory = connectionFactory;
        _topologyBootstrapper = topologyBootstrapper;
        _options = options.Value;
        _logger = logger;
    }

    public Task<RabbitMqQueueDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var connection = _connectionFactory.CreateConnection($"{_options.ClientProvidedName}.queue-inspector");
        using var channel = connection.CreateModel();
        _topologyBootstrapper.EnsureTopology(channel);

        var capturedAt = DateTimeOffset.UtcNow;
        var queues = new[]
        {
            CaptureQueue(channel, _options.ExecutorQueue, isCritical: true, isDeadLetter: false, capturedAt),
            CaptureQueue(channel, _options.PublisherResultsQueue, isCritical: true, isDeadLetter: false, capturedAt),
            CaptureQueue(channel, _options.ExecutorDeadLetterQueue, isCritical: false, isDeadLetter: true, capturedAt),
            CaptureQueue(channel, _options.PublisherResultsDeadLetterQueue, isCritical: false, isDeadLetter: true, capturedAt),
        };

        _logger.LogDebug("Captured RabbitMQ queue diagnostics for {QueueCount} queues.", queues.Length);
        return Task.FromResult(new RabbitMqQueueDiagnosticsSnapshot(capturedAt, queues));
    }

    private RabbitMqQueueSnapshot CaptureQueue(
        IModel channel,
        string queueName,
        bool isCritical,
        bool isDeadLetter,
        DateTimeOffset capturedAt)
    {
        var declaration = channel.QueueDeclarePassive(queueName);
        var messageCount = (long)declaration.MessageCount;
        var consumerCount = (long)declaration.ConsumerCount;

        lock (_syncRoot)
        {
            if (!_queueStates.TryGetValue(queueName, out var state))
            {
                state = new QueueObservationState();
                _queueStates[queueName] = state;
            }

            if (messageCount > 0 && !state.NonEmptySince.HasValue)
            {
                state.NonEmptySince = capturedAt;
            }
            else if (messageCount == 0)
            {
                state.NonEmptySince = null;
            }

            var growth = Math.Max(0, messageCount - state.PreviousMessageCount);
            state.PreviousMessageCount = messageCount;

            return new RabbitMqQueueSnapshot(
                queueName,
                isCritical,
                isDeadLetter,
                messageCount,
                consumerCount,
                state.NonEmptySince,
                growth);
        }
    }

    private sealed class QueueObservationState
    {
        public DateTimeOffset? NonEmptySince { get; set; }

        public long PreviousMessageCount { get; set; }
    }
}
