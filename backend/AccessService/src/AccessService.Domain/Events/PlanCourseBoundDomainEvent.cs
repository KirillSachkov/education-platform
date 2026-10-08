using SharedKernel.DomainEvents;

namespace AccessService.Domain.Events;

/// <summary>
/// Raised when a COURSE-tier plan's catalog snapshot becomes (or stays) bindable:
/// on Create with active+public, price update while active+public, transition into
/// active+public via Publish/Unarchive, or transitions of IsActive/IsPublic that
/// affect catalog visibility. Carries full snapshot — consumers do not need callback.
/// </summary>
public sealed record PlanCourseBoundDomainEvent(
    Guid PlanId,
    Guid AuthorId,
    Guid CourseId,
    long? PriceCents,
    string Currency,
    bool IsActive,
    bool IsPublic) : IDomainEvent;
