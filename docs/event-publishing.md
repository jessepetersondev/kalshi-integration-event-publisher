# Event Publishing And Reliability

The sandbox now runs a two-service RabbitMQ pipeline inside this repository:

- the publisher/API persists orders and outbound command messages in the same transaction
- the executor consumes order commands, performs replay-safe Kalshi execution, and persists follow-up events through its own outbox
- the publisher consumes executor result events and repairs any unapplied result projection gaps automatically

## Publisher command outbox

Publisher order creation no longer does "persist order, publish immediately, fall back to manual review on failure".

Current behavior:

- `OrderSubmissionService` persists the order row and the outbound `order.created` command in one durable transaction
- `PublisherCommandOutboxDispatcher` performs best-effort immediate dispatch for low latency
- `PublisherCommandOutboxBackgroundService` drains pending and leased messages after restarts or broker failures
- publish success requires both broker confirm success and successful routing
- confirm timeout, nack, channel close, connection interruption, and unroutable mandatory returns are recorded as failed attempts
- transient failures move orders into `RetryScheduled`, not manual review
- manual intervention is reserved for retry exhaustion or permanent broker/configuration failures

Publisher outbox persistence lives in:

- `PublisherOutboxMessages`
- `PublisherOutboxAttempts`

## Executor outbox

The executor no longer fire-and-forgets result, inbound, or DLQ publishes.

Current behavior:

- inbound command handling stores execution state and any follow-up event in the same database transaction
- `RabbitMqResultEventPublisher`, `RabbitMqInboundEventPublisher`, and `DeadLetterEventPublisher` queue durable outbox rows
- `ExecutorOutboxBackgroundService` republishes pending messages until they are confirmed and routed
- duplicate outbox message ids are ignored safely during replay

Executor outbox persistence lives in:

- `ExecutorOutboxMessages`
- `ExecutorOutboxAttempts`

## Replay-safe execution

`OrderCreatedHandler` now performs a pre-flight duplicate guard before any `PlaceOrderAsync` call:

- it acquires or resumes a durable `ExecutionRecord`
- it checks prior execution state by publisher order id, client order id, and external mapping
- it uses a lease to prevent concurrent duplicate placements
- it recovers an existing Kalshi order before creating a new one
- replaying the same inbound command does not place a second live order

The executor persists its duplicate-guard and recovery state in:

- `ExecutionRecords`
- `ExternalOrderMappings`
- `ExecutorInboundMessages`

## Routing safety

All critical RabbitMQ publishes now use:

- `mandatory: true`
- publisher confirms
- durable queues and DLQs declared by `RabbitMqTopologyBootstrapper`

An event is treated as published only after:

- RabbitMQ confirms the publish
- the publish is not returned as unroutable

## Repair loops

The pipeline includes automated repair workers:

- `PublisherCommandOutboxBackgroundService` for stuck publisher commands
- `ExecutorOutboxBackgroundService` for stuck executor follow-up events
- `PublisherResultRepairBackgroundService` for persisted-but-unapplied result events
- `ExecutionRepairBackgroundService` for executions that completed the external side effect but still need a terminal result queued/emitted

These workers are replay-safe and rely on deterministic ids plus durable state to close gaps without duplicating business side effects.
