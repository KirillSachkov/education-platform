using System.Collections.Concurrent;
using SharedKernel.DomainEvents;

namespace ProgressService.IntegrationTests.Infrastructure;

/// <summary>
/// Singleton service that records all dispatched domain events.
/// Uses ConcurrentBag to survive across DI scopes (HTTP request creates its own scope).
/// </summary>
public sealed class DomainEventSpy
{
    private readonly ConcurrentBag<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> Events => [.. _events];

    public void Record(IDomainEvent domainEvent) => _events.Add(domainEvent);

    public IReadOnlyList<T> EventsOf<T>()
        where T : IDomainEvent
        => _events.OfType<T>().ToList();

    public void Clear() => _events.Clear();
}
