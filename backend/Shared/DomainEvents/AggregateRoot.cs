using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.DomainEvents;

public abstract class AggregateRoot : IHasDomainEvents
{
    private readonly List<IDomainEvent> _domainEvents = [];

    protected AggregateRoot() { }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public void ClearDomainEvents() => _domainEvents.Clear();

    [SuppressMessage("Design", "CA1030:Use events where appropriate",
        Justification = "Domain event pattern, not .NET event")]
    protected void RaiseDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
