using System.Collections.Concurrent;
using System.Text.Json;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;
using Kalshi.Integration.Application.Trading;
using Kalshi.Integration.Contracts.Reliability;
using Kalshi.Integration.Domain.Common;
using Kalshi.Integration.Domain.Executions;
using Kalshi.Integration.Domain.Orders;
using Kalshi.Integration.Domain.Positions;
using Kalshi.Integration.Domain.TradeIntents;

namespace Kalshi.Integration.Infrastructure.Persistence;

/// <summary>
/// Provides an in-memory trading repository for local development and tests.
/// </summary>
public sealed class InMemoryTradingRepository :
    ITradeIntentRepository,
    IOrderRepository,
    IPositionSnapshotRepository,
    IOrderCommandSubmissionStore,
    IPublisherCommandOutboxStore,
    IExecutorResultProjectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly object _syncRoot = new();
    private readonly ConcurrentDictionary<Guid, TradeIntent> _tradeIntents = new();
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<ExecutionEvent>> _orderEvents = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<(string Stage, string? Details, DateTimeOffset OccurredAt)>> _orderLifecycleEvents = new();
    private readonly ConcurrentDictionary<Guid, ResultEntry> _resultEvents = new();
    private readonly ConcurrentDictionary<Guid, OutboxEntry> _outboxMessages = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<OutboxAttemptEntry>> _outboxAttempts = new();
    private readonly ConcurrentDictionary<string, PositionSnapshot> _positions = new();

    public Task AddTradeIntentAsync(TradeIntent tradeIntent, CancellationToken cancellationToken = default)
    {
        _tradeIntents[tradeIntent.Id] = tradeIntent;
        return Task.CompletedTask;
    }

    public Task<TradeIntent?> GetTradeIntentAsync(Guid tradeIntentId, CancellationToken cancellationToken = default)
    {
        _tradeIntents.TryGetValue(tradeIntentId, out var tradeIntent);
        return Task.FromResult(tradeIntent);
    }

    public Task<TradeIntent?> GetTradeIntentByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        var tradeIntent = _tradeIntents.Values.FirstOrDefault(x => string.Equals(x.CorrelationId, correlationId, StringComparison.Ordinal));
        return Task.FromResult(tradeIntent);
    }

    public Task<TradeIntent?> FindMatchingCancelTradeIntentAsync(
        Guid? targetPublisherOrderId,
        string? targetClientOrderId,
        string? targetExternalOrderId,
        CancellationToken cancellationToken = default)
    {
        var tradeIntent = _tradeIntents.Values
            .Where(x => x.ActionType == TradeIntentActionType.Cancel)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault(x =>
                (targetPublisherOrderId.HasValue && x.TargetPublisherOrderId == targetPublisherOrderId.Value)
                || (!string.IsNullOrWhiteSpace(targetClientOrderId) && string.Equals(x.TargetClientOrderId, targetClientOrderId, StringComparison.Ordinal))
                || (!string.IsNullOrWhiteSpace(targetExternalOrderId) && string.Equals(x.TargetExternalOrderId, targetExternalOrderId, StringComparison.Ordinal)));

        return Task.FromResult(tradeIntent);
    }

    public Task AddOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task UpdateOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task<Order?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(orderId, out var order);
        return Task.FromResult(order);
    }

    public Task<Order?> GetLatestOrderByTradeIntentIdAsync(Guid tradeIntentId, CancellationToken cancellationToken = default)
    {
        var order = _orders.Values
            .Where(x => x.TradeIntent.Id == tradeIntentId)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<Order>> GetOrdersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Order>>(_orders.Values.OrderByDescending(x => x.UpdatedAt).ToArray());

    public Task AddOrderEventAsync(ExecutionEvent executionEvent, CancellationToken cancellationToken = default)
    {
        var queue = _orderEvents.GetOrAdd(executionEvent.OrderId, _ => new ConcurrentQueue<ExecutionEvent>());
        queue.Enqueue(executionEvent);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExecutionEvent>> GetOrderEventsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (_orderEvents.TryGetValue(orderId, out var queue))
        {
            return Task.FromResult<IReadOnlyList<ExecutionEvent>>(queue.ToArray());
        }

        return Task.FromResult<IReadOnlyList<ExecutionEvent>>(Array.Empty<ExecutionEvent>());
    }

    public Task AddOrderLifecycleEventAsync(Guid orderId, string stage, string? details, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        var queue = _orderLifecycleEvents.GetOrAdd(orderId, _ => new ConcurrentQueue<(string Stage, string? Details, DateTimeOffset OccurredAt)>());
        queue.Enqueue((stage, details, occurredAt));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)>> GetOrderLifecycleEventsAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (_orderLifecycleEvents.TryGetValue(orderId, out var queue))
        {
            return Task.FromResult<IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)>>(queue.ToArray());
        }

        return Task.FromResult<IReadOnlyList<(string Stage, string? Details, DateTimeOffset OccurredAt)>>(Array.Empty<(string Stage, string? Details, DateTimeOffset OccurredAt)>());
    }

    public Task<bool> TryAddResultEventAsync(Guid resultEventId, Guid? orderId, string name, string? correlationId, string? idempotencyKey, string payloadJson, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        var added = _resultEvents.TryAdd(resultEventId, new ResultEntry(orderId, name, correlationId, idempotencyKey, payloadJson, occurredAt));
        return Task.FromResult(added);
    }

    public Task UpsertPositionSnapshotAsync(PositionSnapshot positionSnapshot, CancellationToken cancellationToken = default)
    {
        var key = $"{positionSnapshot.Ticker}:{positionSnapshot.Side}";
        _positions[key] = positionSnapshot;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PositionSnapshot>> GetPositionsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PositionSnapshot>>(_positions.Values.OrderBy(p => p.Ticker).ToArray());

    public Task SubmitOrderWithCommandAsync(
        Order order,
        ApplicationEventEnvelope commandEvent,
        PositionSnapshot? initialPositionSnapshot,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (_orders.Values.Any(existing => existing.TradeIntent.Id == order.TradeIntent.Id))
            {
                throw new DomainException($"Trade intent '{order.TradeIntent.Id}' already has an order.");
            }

            if (!string.IsNullOrWhiteSpace(order.ClientOrderId)
                && _orders.Values.Any(existing => string.Equals(existing.ClientOrderId, order.ClientOrderId, StringComparison.Ordinal)))
            {
                throw new DomainException($"Client order id '{order.ClientOrderId}' is already in use.");
            }

            _orders[order.Id] = order;
            GetOrderEventQueue(order.Id).Enqueue(new ExecutionEvent(order.Id, order.CurrentStatus, order.FilledQuantity, order.CreatedAt));
            GetLifecycleQueue(order.Id).Enqueue(("order_created", null, order.CreatedAt));
            GetLifecycleQueue(order.Id).Enqueue(("command_outbox_enqueued", $"commandEventId={commandEvent.Id}", commandEvent.OccurredAt));

            if (initialPositionSnapshot is not null)
            {
                _positions[$"{initialPositionSnapshot.Ticker}:{initialPositionSnapshot.Side}"] = initialPositionSnapshot;
            }

            _outboxMessages[commandEvent.Id] = new OutboxEntry(
                commandEvent.Id,
                order.Id,
                "order",
                JsonSerializer.Serialize(commandEvent, SerializerOptions),
                OutboxMessageStatus.Pending,
                0,
                commandEvent.OccurredAt,
                commandEvent.OccurredAt,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        return Task.CompletedTask;
    }

    public Task<OutboxDispatchItem?> GetAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        if (_outboxMessages.TryGetValue(messageId, out var entry))
        {
            return Task.FromResult<OutboxDispatchItem?>(MapOutboxItem(entry));
        }

        return Task.FromResult<OutboxDispatchItem?>(null);
    }

    public Task<IReadOnlyList<OutboxDispatchItem>> AcquireDueMessagesAsync(
        int maxCount,
        string processorId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        List<OutboxDispatchItem> acquired = [];

        lock (_syncRoot)
        {
            foreach (var entry in _outboxMessages.Values
                         .Where(static x => x.Status == OutboxMessageStatus.Pending)
                         .Where(x => x.NextAttemptAt <= now)
                         .OrderBy(x => x.NextAttemptAt)
                         .ThenBy(x => x.CreatedAt)
                         .Take(Math.Max(1, maxCount)))
            {
                entry.Status = OutboxMessageStatus.InFlight;
                entry.ProcessorId = processorId;
                entry.LeaseExpiresAt = now.Add(leaseDuration);
                acquired.Add(MapOutboxItem(entry));
            }
        }

        return Task.FromResult<IReadOnlyList<OutboxDispatchItem>>(acquired);
    }

    public Task RecordAttemptAsync(
        Guid messageId,
        int attemptNumber,
        string outcome,
        string? failureKind,
        string? errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        if (_outboxMessages.TryGetValue(messageId, out var entry))
        {
            entry.AttemptCount = Math.Max(entry.AttemptCount, attemptNumber);
            entry.LastAttemptAt = occurredAt;
            entry.LastError = errorMessage;
            entry.LastFailureKind = failureKind;
        }

        var queue = _outboxAttempts.GetOrAdd(messageId, _ => new ConcurrentQueue<OutboxAttemptEntry>());
        queue.Enqueue(new OutboxAttemptEntry(attemptNumber, outcome, failureKind, errorMessage, occurredAt));
        return Task.CompletedTask;
    }

    public Task MarkPublishedAsync(Guid messageId, DateTimeOffset publishedAt, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (!_outboxMessages.TryGetValue(messageId, out var entry))
            {
                return Task.CompletedTask;
            }

            entry.Status = OutboxMessageStatus.Published;
            entry.PublishedAt = publishedAt;
            entry.LeaseExpiresAt = null;
            entry.ProcessorId = null;

            if (_orders.TryGetValue(entry.AggregateId, out var order))
            {
                order.MarkPublishConfirmed(messageId, publishedAt);
                GetLifecycleQueue(order.Id).Enqueue(("publish_confirmed", $"commandEventId={messageId}", publishedAt));
            }
        }

        return Task.CompletedTask;
    }

    public Task ScheduleRetryAsync(
        Guid messageId,
        DateTimeOffset nextAttemptAt,
        string failureKind,
        string errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (!_outboxMessages.TryGetValue(messageId, out var entry))
            {
                return Task.CompletedTask;
            }

            entry.Status = OutboxMessageStatus.Pending;
            entry.NextAttemptAt = nextAttemptAt;
            entry.LeaseExpiresAt = null;
            entry.ProcessorId = null;
            entry.LastError = errorMessage;
            entry.LastFailureKind = failureKind;

            if (_orders.TryGetValue(entry.AggregateId, out var order))
            {
                order.MarkRetryScheduled(errorMessage, messageId, occurredAt);
                GetLifecycleQueue(order.Id).Enqueue(("publish_retry_scheduled", errorMessage, occurredAt));
            }
        }

        return Task.CompletedTask;
    }

    public Task MarkManualInterventionRequiredAsync(
        Guid messageId,
        string failureKind,
        string errorMessage,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (!_outboxMessages.TryGetValue(messageId, out var entry))
            {
                return Task.CompletedTask;
            }

            entry.Status = OutboxMessageStatus.ManualInterventionRequired;
            entry.LeaseExpiresAt = null;
            entry.ProcessorId = null;
            entry.LastError = errorMessage;
            entry.LastFailureKind = failureKind;

            if (_orders.TryGetValue(entry.AggregateId, out var order))
            {
                order.MarkManualInterventionRequired(errorMessage, messageId, occurredAt);
                GetLifecycleQueue(order.Id).Enqueue(("manual_intervention_required", errorMessage, occurredAt));
            }
        }

        return Task.CompletedTask;
    }

    public Task<int> ReleaseExpiredLeasesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var released = 0;

        lock (_syncRoot)
        {
            foreach (var entry in _outboxMessages.Values.Where(x => x.Status == OutboxMessageStatus.InFlight && x.LeaseExpiresAt <= now))
            {
                entry.Status = OutboxMessageStatus.Pending;
                entry.LeaseExpiresAt = null;
                entry.ProcessorId = null;
                released++;
            }
        }

        return Task.FromResult(released);
    }

    public Task<OutboxHealthSnapshot> GetHealthSnapshotAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var pending = _outboxMessages.Values
            .Where(x => x.Status == OutboxMessageStatus.Pending || x.Status == OutboxMessageStatus.InFlight)
            .ToArray();

        return Task.FromResult(
            new OutboxHealthSnapshot(
                pending.LongLength,
                _outboxMessages.Values.LongCount(x => x.Status == OutboxMessageStatus.ManualInterventionRequired),
                pending.OrderBy(x => x.CreatedAt).FirstOrDefault()?.CreatedAt));
    }

    public Task<bool> ApplyExecutorResultAsync(
        ApplicationEventEnvelope resultEvent,
        ResultProjectionMutation mutation,
        CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (_resultEvents.TryGetValue(resultEvent.Id, out var existing) && existing.AppliedAt.HasValue)
            {
                return Task.FromResult(false);
            }

            if (!_resultEvents.TryGetValue(resultEvent.Id, out var pending))
            {
                pending = new ResultEntry(
                    mutation.OrderId,
                    resultEvent.Name,
                    resultEvent.CorrelationId,
                    resultEvent.IdempotencyKey,
                    JsonSerializer.Serialize(resultEvent, SerializerOptions),
                    resultEvent.OccurredAt);
                _resultEvents[resultEvent.Id] = pending;
            }

            pending.ApplyAttemptCount++;
            pending.LastApplyAttemptAt = DateTimeOffset.UtcNow;

            if (!_orders.TryGetValue(mutation.OrderId, out var order))
            {
                throw new KeyNotFoundException($"Order '{mutation.OrderId}' was not found.");
            }

            var previousStatus = order.CurrentStatus;
            order.ApplyResult(
                mutation.ResultStatus,
                mutation.NextStatus,
                mutation.FilledQuantity,
                mutation.Details,
                mutation.ExternalOrderId,
                mutation.ClientOrderId,
                mutation.CommandEventId,
                resultEvent.OccurredAt);

            _orders[order.Id] = order;
            GetLifecycleQueue(order.Id).Enqueue((resultEvent.Name, mutation.Details, resultEvent.OccurredAt));

            if (mutation.NextStatus.HasValue && mutation.NextStatus.Value != previousStatus)
            {
                GetOrderEventQueue(order.Id).Enqueue(new ExecutionEvent(order.Id, mutation.NextStatus.Value, order.FilledQuantity, resultEvent.OccurredAt));
            }

            if (mutation.UpdatePositionSnapshot && order.TradeIntent.Side.HasValue && order.TradeIntent.LimitPrice.HasValue)
            {
                _positions[$"{order.TradeIntent.Ticker}:{order.TradeIntent.Side.Value}"] = new PositionSnapshot(
                    order.TradeIntent.Ticker,
                    order.TradeIntent.Side.Value,
                    order.FilledQuantity,
                    order.TradeIntent.LimitPrice.Value,
                    resultEvent.OccurredAt);
            }

            pending.AppliedAt = DateTimeOffset.UtcNow;
            pending.LastError = null;
            return Task.FromResult(true);
        }
    }

    private ConcurrentQueue<ExecutionEvent> GetOrderEventQueue(Guid orderId)
        => _orderEvents.GetOrAdd(orderId, _ => new ConcurrentQueue<ExecutionEvent>());

    private ConcurrentQueue<(string Stage, string? Details, DateTimeOffset OccurredAt)> GetLifecycleQueue(Guid orderId)
        => _orderLifecycleEvents.GetOrAdd(orderId, _ => new ConcurrentQueue<(string Stage, string? Details, DateTimeOffset OccurredAt)>());

    private static OutboxDispatchItem MapOutboxItem(OutboxEntry entry)
        => new(
            entry.Id,
            entry.AggregateId,
            entry.AggregateType,
            entry.PayloadJson,
            entry.AttemptCount,
            entry.CreatedAt,
            entry.LastAttemptAt,
            entry.NextAttemptAt,
            entry.LeaseExpiresAt,
            entry.LastError,
            entry.Status);

    private sealed class ResultEntry
    {
        public ResultEntry(
            Guid? orderId,
            string name,
            string? correlationId,
            string? idempotencyKey,
            string payloadJson,
            DateTimeOffset occurredAt)
        {
            OrderId = orderId;
            Name = name;
            CorrelationId = correlationId;
            IdempotencyKey = idempotencyKey;
            PayloadJson = payloadJson;
            OccurredAt = occurredAt;
        }

        public Guid? OrderId { get; }
        public string Name { get; }
        public string? CorrelationId { get; }
        public string? IdempotencyKey { get; }
        public string PayloadJson { get; }
        public DateTimeOffset OccurredAt { get; }
        public int ApplyAttemptCount { get; set; }
        public DateTimeOffset? LastApplyAttemptAt { get; set; }
        public DateTimeOffset? AppliedAt { get; set; }
        public string? LastError { get; set; }
    }

    private sealed class OutboxEntry
    {
        public OutboxEntry(
            Guid id,
            Guid aggregateId,
            string aggregateType,
            string payloadJson,
            OutboxMessageStatus status,
            int attemptCount,
            DateTimeOffset createdAt,
            DateTimeOffset nextAttemptAt,
            DateTimeOffset? lastAttemptAt,
            DateTimeOffset? publishedAt,
            string? processorId,
            DateTimeOffset? leaseExpiresAt,
            string? lastError,
            string? lastFailureKind)
        {
            Id = id;
            AggregateId = aggregateId;
            AggregateType = aggregateType;
            PayloadJson = payloadJson;
            Status = status;
            AttemptCount = attemptCount;
            CreatedAt = createdAt;
            NextAttemptAt = nextAttemptAt;
            LastAttemptAt = lastAttemptAt;
            PublishedAt = publishedAt;
            ProcessorId = processorId;
            LeaseExpiresAt = leaseExpiresAt;
            LastError = lastError;
            LastFailureKind = lastFailureKind;
        }

        public Guid Id { get; }
        public Guid AggregateId { get; }
        public string AggregateType { get; }
        public string PayloadJson { get; }
        public OutboxMessageStatus Status { get; set; }
        public int AttemptCount { get; set; }
        public DateTimeOffset CreatedAt { get; }
        public DateTimeOffset NextAttemptAt { get; set; }
        public DateTimeOffset? LastAttemptAt { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public string? ProcessorId { get; set; }
        public DateTimeOffset? LeaseExpiresAt { get; set; }
        public string? LastError { get; set; }
        public string? LastFailureKind { get; set; }
    }

    private sealed record OutboxAttemptEntry(
        int AttemptNumber,
        string Outcome,
        string? FailureKind,
        string? ErrorMessage,
        DateTimeOffset OccurredAt);
}
