using System.Collections.Concurrent;
using Kalshi.Integration.Application.Abstractions;
using Kalshi.Integration.Application.Events;

namespace Kalshi.Integration.Infrastructure.Operations;

/// <summary>
/// Publishes in memory application event.
/// </summary>


public sealed class InMemoryApplicationEventPublisher : IApplicationEventPublisher
{
    private readonly ConcurrentQueue<ApplicationEventEnvelope> _publishedEvents = new();
    private readonly ConcurrentDictionary<Guid, Func<ApplicationEventEnvelope, CancellationToken, Task>> _subscribers = new();

    public async Task PublishAsync(ApplicationEventEnvelope applicationEvent, CancellationToken cancellationToken = default)
    {
        _publishedEvents.Enqueue(applicationEvent);

        var subscribers = _subscribers.Values.ToArray();
        foreach (var subscriber in subscribers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await subscriber(applicationEvent, cancellationToken);
        }
    }

    public IDisposable Subscribe(Func<ApplicationEventEnvelope, CancellationToken, Task> subscriber)
    {
        var id = Guid.NewGuid();
        _subscribers[id] = subscriber;
        return new Subscription(_subscribers, id);
    }

    public IReadOnlyList<ApplicationEventEnvelope> GetPublishedEvents() => _publishedEvents.ToArray();

    public void Reset()
    {
        while (_publishedEvents.TryDequeue(out _))
        {
        }

        _subscribers.Clear();
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, Func<ApplicationEventEnvelope, CancellationToken, Task>> _subscribers;
        private readonly Guid _id;
        private bool _disposed;

        public Subscription(ConcurrentDictionary<Guid, Func<ApplicationEventEnvelope, CancellationToken, Task>> subscribers, Guid id)
        {
            _subscribers = subscribers;
            _id = id;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _subscribers.TryRemove(_id, out _);
            _disposed = true;
        }
    }
}
