using SharedKernel.DomainEvents;

namespace AccessService.Domain.Events;

/// <summary>
/// Поднимается при создании grant'а / Raised when a plan grant is created.
/// </summary>
public sealed record PlanGrantCreatedDomainEvent(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    PlanGrantSource Source,
    Guid? SourceRef) : IDomainEvent;
