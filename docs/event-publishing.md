# Event Publishing Extension Path

The sandbox now has a **clean application-boundary publishing abstraction** for outbound application events:

- `IApplicationEventPublisher` lives in `Kalshi.Integration.Application`
- `ApplicationEventEnvelope` defines the transport-agnostic event contract
- `InMemoryApplicationEventPublisher` supports the default in-process MVP path
- `RabbitMqApplicationEventPublisher` provides a broker-backed adapter behind the same application interface

## Current behavior

The implementation now supports two publish modes selected via configuration:

- `EventPublishing:Provider=InMemory`
  - successful API workflows publish application events in-process
  - the in-memory publisher keeps a local event history for inspection
  - tests can subscribe to events directly without a broker
- `EventPublishing:Provider=RabbitMq`
  - successful API workflows publish serialized `ApplicationEventEnvelope` messages to RabbitMQ
  - the adapter declares the configured exchange before publishing
  - correlation/idempotency/resource metadata is mapped into RabbitMQ headers and message properties

This keeps the application boundary clean while still demonstrating a real broker adapter.

## Publisher responsibilities

The publisher abstraction is responsible for:

- accepting a transport-agnostic application event envelope
- dispatching that event through the configured implementation
- avoiding any dependency on a concrete broker in application/domain code

The publisher abstraction is **not** responsible for:

- business validation
- workflow orchestration
- domain state transitions
- broker-specific retry policy semantics
- queue / topic provisioning

Those concerns stay in the appropriate layer.

## Current event shape

`ApplicationEventEnvelope` carries:

- event id
- category
- event name
- resource id
- correlation id
- idempotency key
- string-based attributes
- occurred-at timestamp

The envelope stays generic so concrete broker adapters can serialize it without pushing broker concepts into the application layer.

## RabbitMQ adapter details

`RabbitMqApplicationEventPublisher` currently:

- uses `RabbitMqOptions` from configuration
- publishes to a durable topic exchange
- builds routing keys from `{RoutingKeyPrefix}.{Category}.{EventName}`
- normalizes event names such as `order.created` and `execution-update.applied`
- writes message headers for:
  - `event-id`
  - `category`
  - `event-name`
  - `occurred-at`
  - `resource-id` when present
  - `correlation-id` when present
  - `idempotency-key` when present
  - `attribute:*` for envelope attributes

Default configuration lives in `src/Kalshi.Integration.Api/appsettings.json`.

## Future path

If this sandbox later needs Azure-specific messaging signal, keep the application contract unchanged and add another infrastructure implementation such as:

- `AzureServiceBusApplicationEventPublisher`

That adapter should follow the same pattern: map `ApplicationEventEnvelope` into broker-native messages without leaking broker concepts into the application layer.
