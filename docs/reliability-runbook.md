# Reliability Runbook

This runbook covers investigation and safe repair of the durable order pipeline.

## Immediate triage

1. Check readiness on both services.
2. Review recent operational issues.
3. Confirm critical RabbitMQ queues have consumers and acceptable backlog age.
4. Check DLQ depth before attempting any manual replay.

Key readiness endpoints:

- publisher API: `/health/ready`
- executor: `/health/ready`

## Durable state to inspect

Publisher:

- `Orders`
- `PublisherOutboxMessages`
- `PublisherOutboxAttempts`
- `ResultEvents`
- `OperationalIssues`

Executor:

- `ExecutionRecords`
- `ExternalOrderMappings`
- `ExecutorInboundMessages`
- `ExecutorOutboxMessages`
- `ExecutorOutboxAttempts`
- `ExecutorOperationalIssues`

## What healthy recovery looks like

Transient broker or process failures should recover automatically:

- publisher outbox rows move from `Pending` or `InFlight` to `Published`
- executor outbox rows move from `Pending` or `InFlight` to `Published`
- `ResultEvents.AppliedAt` is eventually populated
- `ExecutionRecords.TerminalResultQueuedAt` and `TerminalResultPublishedAt` are eventually populated

Manual action is only expected when outbox rows enter `ManualInterventionRequired` or when a permanent broker/configuration issue is confirmed.

## Safe investigation queries

SQLite examples:

```sql
select Id, Status, AttemptCount, NextAttemptAt, LastFailureKind, LastError
from PublisherOutboxMessages
where Status <> 'Published'
order by CreatedAt;

select Id, Status, AttemptCount, NextAttemptAt, LastFailureKind, LastError
from ExecutorOutboxMessages
where Status <> 'Published'
order by CreatedAt;

select Id, Name, OrderId, AppliedAt, ApplyAttemptCount, LastError
from ResultEvents
where AppliedAt is null
order by OccurredAt;

select PublisherOrderId, ClientOrderId, ExternalOrderId, Status, TerminalResultQueuedAt, TerminalResultPublishedAt, LastError
from ExecutionRecords
order by UpdatedAt desc;
```

## Repair guidance

Publisher command gap:

- verify RabbitMQ connectivity and routing first
- if rows are still `Pending` or `InFlight`, restart is safe because the background outbox worker is restart-safe
- if rows reached `ManualInterventionRequired`, inspect `LastFailureKind` and fix the underlying broker or routing issue before replay

Executor terminal-result gap:

- inspect `ExecutionRecords` and `ExecutorOutboxMessages`
- if an execution has an external order id but no published terminal event, the execution repair worker should requeue the missing terminal event safely
- replaying the same inbound `order.created` message is safe; the duplicate guard prevents a second live order

Publisher result-consumption gap:

- inspect `ResultEvents` where `AppliedAt is null`
- the publisher repair worker re-applies persisted result envelopes safely without duplicating order state transitions

DLQ growth:

- inspect the dead-letter payload and the paired execution/order ids
- fix the root cause before moving messages back into the live flow
- never manually replay a command without checking `ExecutionRecords` and `ExternalOrderMappings` first

## Things not to do

- do not delete outbox or execution rows to "unstick" the pipeline
- do not manually republish a command until the duplicate guard state has been reviewed
- do not bypass readiness failures and assume the queue path is healthy

## Expected alert conditions

Investigate immediately when any of the following occurs:

- outbox oldest pending age exceeds the unhealthy threshold
- retry exhaustion increments
- RabbitMQ reconnect failures repeat
- critical queue consumer count drops to zero
- DLQ size grows
- duplicate-guard hits spike unexpectedly
