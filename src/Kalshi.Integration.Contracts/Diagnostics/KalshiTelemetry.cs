using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Kalshi.Integration.Contracts.Diagnostics;

/// <summary>
/// Defines telemetry names and instruments for kalshi.
/// </summary>
public static class KalshiTelemetry
{
    /// <summary>
    /// Gets the activity source name used by Kalshi integration traces.
    /// </summary>
    public const string ActivitySourceName = "Kalshi.Integration";

    /// <summary>
    /// Gets the meter name used by Kalshi integration metrics.
    /// </summary>
    public const string MeterName = "Kalshi.Integration";

    /// <summary>
    /// Gets the activity source used to create Kalshi integration trace spans.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <summary>
    /// Gets the meter used to publish Kalshi integration metrics.
    /// </summary>
    public static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Gets the histogram that records inbound HTTP server request duration in milliseconds.
    /// </summary>
    public static readonly Histogram<double> HttpServerRequestDurationMs = Meter.CreateHistogram<double>(
        "kalshi.http.server.request.duration",
        unit: "ms",
        description: "Duration of inbound HTTP requests handled by the API.");

    /// <summary>
    /// Gets the histogram that records outbound dependency-call duration in milliseconds.
    /// </summary>
    public static readonly Histogram<double> OutboundDependencyDurationMs = Meter.CreateHistogram<double>(
        "kalshi.dependency.call.duration",
        unit: "ms",
        description: "Duration of outbound dependency calls issued by the application.");

    /// <summary>
    /// Gets the counter that records retry exhaustion events across durable reliability flows.
    /// </summary>
    public static readonly Counter<long> ReliabilityRetryExhaustedTotal = Meter.CreateCounter<long>(
        "kalshi.reliability.retry_exhausted.total",
        description: "Count of reliability workflows that exhausted automatic retries.");

    /// <summary>
    /// Gets the counter that records replay-safe duplicate guard hits.
    /// </summary>
    public static readonly Counter<long> DuplicateGuardHitsTotal = Meter.CreateCounter<long>(
        "kalshi.reliability.duplicate_guard_hits.total",
        description: "Count of duplicate or replay-safe execution recoveries that avoided repeating an external side effect.");

    /// <summary>
    /// Gets the counter that records broker-side publish failures classified by failure kind.
    /// </summary>
    public static readonly Counter<long> RabbitMqPublishFailuresTotal = Meter.CreateCounter<long>(
        "kalshi.rabbitmq.publish_failures.total",
        description: "Count of RabbitMQ publish attempts that failed confirmation, routing, or connection checks.");

    /// <summary>
    /// Gets the counter that records RabbitMQ reconnect or connection-establishment failures.
    /// </summary>
    public static readonly Counter<long> RabbitMqReconnectFailuresTotal = Meter.CreateCounter<long>(
        "kalshi.rabbitmq.reconnect_failures.total",
        description: "Count of RabbitMQ reconnect or connection-establishment failures.");

    /// <summary>
    /// Gets the histogram that samples durable outbox pending counts over time.
    /// </summary>
    public static readonly Histogram<long> OutboxPendingCount = Meter.CreateHistogram<long>(
        "kalshi.reliability.outbox.pending_count",
        unit: "messages",
        description: "Sampled count of outbox messages still pending or in flight.");

    /// <summary>
    /// Gets the histogram that samples the age of the oldest pending outbox message.
    /// </summary>
    public static readonly Histogram<double> OutboxOldestPendingAgeMs = Meter.CreateHistogram<double>(
        "kalshi.reliability.outbox.oldest_pending_age",
        unit: "ms",
        description: "Sampled age of the oldest pending or in-flight outbox message.");

    /// <summary>
    /// Gets the histogram that samples RabbitMQ queue backlog counts.
    /// </summary>
    public static readonly Histogram<long> RabbitMqQueueBacklogCount = Meter.CreateHistogram<long>(
        "kalshi.rabbitmq.queue.backlog_count",
        unit: "messages",
        description: "Sampled RabbitMQ queue backlog count.");

    /// <summary>
    /// Gets the histogram that samples how long a RabbitMQ queue has remained non-empty.
    /// </summary>
    public static readonly Histogram<double> RabbitMqQueueBacklogAgeMs = Meter.CreateHistogram<double>(
        "kalshi.rabbitmq.queue.backlog_age",
        unit: "ms",
        description: "Sampled age of a continuously non-empty RabbitMQ queue backlog.");

    /// <summary>
    /// Gets the histogram that samples RabbitMQ consumer counts on monitored queues.
    /// </summary>
    public static readonly Histogram<long> RabbitMqQueueConsumerCount = Meter.CreateHistogram<long>(
        "kalshi.rabbitmq.queue.consumer_count",
        unit: "consumers",
        description: "Sampled consumer count on monitored RabbitMQ queues.");

    /// <summary>
    /// Gets the histogram that samples RabbitMQ dead-letter queue sizes.
    /// </summary>
    public static readonly Histogram<long> RabbitMqDeadLetterQueueSize = Meter.CreateHistogram<long>(
        "kalshi.rabbitmq.dlq.size",
        unit: "messages",
        description: "Sampled size of RabbitMQ dead-letter queues.");

    /// <summary>
    /// Gets the counter that records growth observed on RabbitMQ dead-letter queues.
    /// </summary>
    public static readonly Counter<long> RabbitMqDeadLetterQueueGrowthTotal = Meter.CreateCounter<long>(
        "kalshi.rabbitmq.dlq.growth.total",
        description: "Count of additional messages observed arriving in RabbitMQ dead-letter queues.");
}
