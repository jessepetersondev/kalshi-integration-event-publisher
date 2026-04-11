using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Samples RabbitMQ queue state and derives backlog-age and growth signals over time.
/// </summary>
public sealed class RabbitMqQueueInspector(
    IConnectionFactory connectionFactory,
    RabbitMqTopologyBootstrapper topologyBootstrapper,
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqQueueInspector> logger)
{
    private readonly IConnectionFactory _connectionFactory = connectionFactory;
    private readonly RabbitMqTopologyBootstrapper _topologyBootstrapper = topologyBootstrapper;
    private readonly RabbitMqOptions _options = options.Value;
    private readonly ILogger<RabbitMqQueueInspector> _logger = logger;
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, QueueObservationState> _queueStates = new(StringComparer.Ordinal);

    public Task<RabbitMqQueueDiagnosticsSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using IConnection connection = _connectionFactory.CreateConnection($"{_options.ClientProvidedName}.queue-inspector");
        using IModel channel = connection.CreateModel();
        _topologyBootstrapper.EnsureTopology(channel);

        DateTimeOffset capturedAt = DateTimeOffset.UtcNow;
        RabbitMqQueueSnapshot[] queues =
        [
            CaptureQueue(channel, _options.ExecutorQueue, isCritical: true, isDeadLetter: false, capturedAt),
            CaptureQueue(channel, _options.PublisherResultsQueue, isCritical: true, isDeadLetter: false, capturedAt),
            CaptureQueue(channel, _options.ExecutorDeadLetterQueue, isCritical: false, isDeadLetter: true, capturedAt),
            CaptureQueue(channel, _options.PublisherResultsDeadLetterQueue, isCritical: false, isDeadLetter: true, capturedAt),
        ];

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
        QueueDeclareOk declaration = channel.QueueDeclarePassive(queueName);
        long messageCount = (long)declaration.MessageCount;
        long consumerCount = (long)declaration.ConsumerCount;

        lock (_syncRoot)
        {
            if (!_queueStates.TryGetValue(queueName, out QueueObservationState? state))
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

            long growth = Math.Max(0, messageCount - state.PreviousMessageCount);
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
