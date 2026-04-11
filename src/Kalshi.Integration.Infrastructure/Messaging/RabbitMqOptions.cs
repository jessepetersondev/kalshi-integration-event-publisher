using System.ComponentModel.DataAnnotations;

namespace Kalshi.Integration.Infrastructure.Messaging;

/// <summary>
/// Configures the RabbitMQ connectivity and topology used for published events.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    [Required]
    public string HostName { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 5672;

    [Required]
    public string VirtualHost { get; set; } = "/";

    [Required]
    public string UserName { get; set; } = "guest";

    [Required]
    public string Password { get; set; } = "guest";

    [Required]
    public string Exchange { get; set; } = "kalshi.integration.events";

    [Required]
    public string ExchangeType { get; set; } = "topic";

    [Required]
    public string RoutingKeyPrefix { get; set; } = "kalshi.integration";

    public bool Mandatory { get; set; } = true;

    [Required]
    public string PublisherResultsQueue { get; set; } = "kalshi.integration.event-publisher.results";

    [Required]
    public string PublisherResultsDeadLetterQueue { get; set; } = "kalshi.integration.event-publisher.results.dlq";

    [Required]
    public string ExecutorQueue { get; set; } = "kalshi.integration.executor";

    [Required]
    public string ExecutorDeadLetterQueue { get; set; } = "kalshi.integration.executor.dlq";

    [Required]
    public string ExecutorRoutingKeyBinding { get; set; } = "kalshi.integration.trading.#";

    [Required]
    public string ResultsRoutingKeyBinding { get; set; } = "kalshi.integration.results.#";

    [Range(0, 10)]
    public int PublishRetryAttempts { get; set; } = 2;

    [Range(1, 30000)]
    public int PublishRetryDelayMilliseconds { get; set; } = 250;

    [Range(1, 30000)]
    public int PublishConfirmTimeoutMilliseconds { get; set; } = 5000;

    [Range(1, 500)]
    public int OutboxBatchSize { get; set; } = 25;

    [Range(50, 60000)]
    public int OutboxPollingIntervalMilliseconds { get; set; } = 1000;

    [Range(100, 60000)]
    public int QueueMonitoringIntervalMilliseconds { get; set; } = 5000;

    public bool EnableReliabilityMonitoring { get; set; } = true;

    public bool EnableQueueHealthChecks { get; set; } = true;

    [Range(5, 600)]
    public int OutboxLeaseDurationSeconds { get; set; } = 30;

    [Range(1, 100)]
    public int OutboxMaxAttempts { get; set; } = 10;

    [Range(50, 60000)]
    public int OutboxInitialRetryDelayMilliseconds { get; set; } = 500;

    [Range(100, 300000)]
    public int OutboxMaxRetryDelayMilliseconds { get; set; } = 30000;

    [Range(0, 10000)]
    public int OutboxJitterMaxMilliseconds { get; set; } = 250;

    [Range(1, 500)]
    public int RepairBatchSize { get; set; } = 25;

    [Range(1, 3600)]
    public int RepairGraceSeconds { get; set; } = 30;

    [Range(1, 86400)]
    public int OutboxDegradedAgeSeconds { get; set; } = 30;

    [Range(1, 86400)]
    public int OutboxUnhealthyAgeSeconds { get; set; } = 300;

    [Range(1, 86400)]
    public int CriticalQueueBacklogDegradedAgeSeconds { get; set; } = 30;

    [Range(1, 86400)]
    public int CriticalQueueBacklogUnhealthyAgeSeconds { get; set; } = 300;

    public bool EnableResultConsumer { get; set; } = true;

    [Range(1, 300)]
    public int ConsumerRecoveryDelaySeconds { get; set; } = 5;

    [Required]
    public string ClientProvidedName { get; set; } = "kalshi-integration-event-publisher";
}
