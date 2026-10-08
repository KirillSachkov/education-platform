using SharedKernel.DomainEvents;

namespace AccessService.Domain.Events;

/// <summary>
/// Raised when a COURSE-tier plan stops being eligible for catalog binding —
/// archived, or transitioned out of active+public state. Consumers should drop
/// any cached catalog snapshot for this (plan, course) pair.
/// </summary>
public sealed record PlanCourseUnboundDomainEvent(
    Guid PlanId,
    Guid CourseId) : IDomainEvent;
