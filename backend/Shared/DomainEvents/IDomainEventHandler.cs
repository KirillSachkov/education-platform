using System.Diagnostics.CodeAnalysis;
using CSharpFunctionalExtensions;

namespace SharedKernel.DomainEvents;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Domain event handler is the correct domain term")]
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task<UnitResult<Error>> Handle(TEvent domainEvent, CancellationToken ct);
}
