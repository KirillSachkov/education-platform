using SharedKernel.DomainEvents;

namespace AccessService.Domain.Events;

/// <summary>
/// Поднимается при отзыве grant'а / Raised when a plan grant is revoked.
/// </summary>
public sealed record PlanGrantRevokedDomainEvent(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    string? Reason) : IDomainEvent;
