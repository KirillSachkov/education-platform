using SharedKernel.DomainEvents;

namespace AccessService.Domain.Events;

/// <summary>
/// Поднимается когда grant перешёл в EXPIRED по истечении TTL /
/// Raised when a grant transitions to EXPIRED via TTL elapsed.
/// </summary>
public sealed record PlanGrantExpiredDomainEvent(
    Guid GrantId,
    Guid UserId,
    Guid PlanId) : IDomainEvent;
