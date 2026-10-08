using SharedKernel.DomainEvents;

namespace AccessService.Domain.Events;

/// <summary>
/// Поднимается при создании плана / Raised when a plan is created.
/// </summary>
public sealed record PlanCreatedDomainEvent(Guid PlanId, Guid AuthorId, PlanTier Tier) : IDomainEvent;
