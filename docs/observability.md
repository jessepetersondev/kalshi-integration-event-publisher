# Observability

The publisher API and executor service emit OpenTelemetry traces and metrics, structured logs, readiness health, and operational-issue records for the critical reliability paths.

## Traces

Both .NET services emit traces for:

- inbound ASP.NET Core requests
- outbound `HttpClient` calls
- EF Core database operations

The API and executor both add `KalshiTelemetry.ActivitySourceName`, so outbox publication, duplicate-guard recovery, and result projection paths can be correlated with request and dependency spans.

## Metrics

Custom reliability metrics now include:

- `kalshi.reliability.retry_exhausted.total`
- `kalshi.reliability.duplicate_guard_hits.total`
- `kalshi.rabbitmq.publish_failures.total`
- `kalshi.rabbitmq.reconnect_failures.total`
- `kalshi.outbox.pending.count`
- `kalshi.outbox.oldest_pending_age.ms`
- `kalshi.rabbitmq.queue.backlog.count`
- `kalshi.rabbitmq.queue.backlog_age.ms`
- `kalshi.rabbitmq.queue.consumer_count`
- `kalshi.rabbitmq.dead_letter_queue.size`
- `kalshi.rabbitmq.dead_letter_queue.growth.total`

These sit alongside the existing request, dependency, runtime, and process instrumentation.

## Health checks

Publisher API:

- `self`
- `database`
- `publisher-outbox`
- `rabbitmq-queues` when RabbitMQ publishing or result consumption is enabled
- `node-gateway` when configured for readiness

Executor:

- `self`
- `database`
- `executor-outbox`
- `rabbitmq-queues`

Readiness checks degrade or fail automatically for:

- outbox backlog age beyond configured thresholds
- retry exhaustion/manual intervention requirements
- RabbitMQ inspection failures
- DLQ growth or non-empty DLQs
- critical queues with `consumer_count == 0`
- critical queue backlog age beyond configured thresholds

## Logs and operational issues

Both services record actionable reliability problems into durable operational issue tables:

- publisher: `OperationalIssues`
- executor: `ExecutorOperationalIssues`

Typical issue categories include:

- publish retry exhaustion
- RabbitMQ reconnect failure
- queue inspection failure
- repair-loop failure

## Export configuration

The API and executor both support OTLP export through:

- `OpenTelemetry__OtlpEndpoint`

Example:

```bash
export OpenTelemetry__OtlpEndpoint='http://localhost:4317'
```

Health endpoints:

- API: `/health/live`, `/health/ready`
- Executor: `/health/live`, `/health/ready`
