using AccessService.Domain;
using AccessService.Domain.Events;
using CSharpFunctionalExtensions;
using SharedKernel;
using SharedKernel.DomainEvents;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// Pure domain tests for <see cref="PlanCourseBoundDomainEvent"/> /
/// <see cref="PlanCourseUnboundDomainEvent"/> emission on Plan lifecycle transitions.
/// Catalog visibility = COURSE-tier + CourseId + IsActive + IsPublic.
/// </summary>
public class PlanCourseEventTests
{
    private static readonly PlanSlug Slug = PlanSlug.Of("course-plan").Value;
    private static readonly PlanDisplayName Name = PlanDisplayName.Of("Course Plan").Value;

    private static Plan CreateCoursePlan(Guid? courseId = null)
    {
        Result<Plan, Error> result = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.COURSE,
            slug: Slug,
            displayName: Name,
            courseIds: [courseId ?? Guid.NewGuid()],
            requestedCapabilities: null);
        return result.Value;
    }

    private static T? SingleEvent<T>(Plan plan) where T : class, IDomainEvent =>
        plan.DomainEvents.OfType<T>().SingleOrDefault();

    [Fact]
    public void Create_COURSE_does_not_raise_Bound_event_because_not_public_yet()
    {
        Plan plan = CreateCoursePlan();
        Assert.Null(SingleEvent<PlanCourseBoundDomainEvent>(plan));
    }

    [Fact]
    public void Publish_COURSE_with_courseId_raises_Bound_event_with_full_snapshot()
    {
        Plan plan = CreateCoursePlan();
        plan.UpdatePrice(990_00, "RUB");
        plan.ClearDomainEvents();

        plan.Publish();

        PlanCourseBoundDomainEvent bound = Assert.IsType<PlanCourseBoundDomainEvent>(plan.DomainEvents.Single());
        Assert.Equal(plan.Id, bound.PlanId);
        Assert.Equal(plan.AuthorId, bound.AuthorId);
        Assert.Equal(plan.FirstCourseId!.Value, bound.CourseId);
        Assert.Equal(990_00, bound.PriceCents);
        Assert.Equal("RUB", bound.Currency);
        Assert.True(bound.IsActive);
        Assert.True(bound.IsPublic);
    }

    [Fact]
    public void UpdatePrice_on_active_public_COURSE_raises_Bound_event()
    {
        Plan plan = CreateCoursePlan();
        plan.Publish();
        plan.ClearDomainEvents();

        plan.UpdatePrice(1_500_00, "RUB");

        PlanCourseBoundDomainEvent bound = Assert.IsType<PlanCourseBoundDomainEvent>(plan.DomainEvents.Single());
        Assert.Equal(1_500_00, bound.PriceCents);
    }

    [Fact]
    public void UpdatePrice_on_unpublished_COURSE_does_not_raise_Bound_event()
    {
        Plan plan = CreateCoursePlan();
        plan.ClearDomainEvents();

        plan.UpdatePrice(1_500_00, "RUB");

        Assert.Null(SingleEvent<PlanCourseBoundDomainEvent>(plan));
    }

    [Fact]
    public void Publish_COURSE_without_price_raises_Bound_event_with_null_PriceCents()
    {
        // PriceCents is left null (no UpdatePrice call) — Bound event must carry null,
        // not 0, so consumers can distinguish "no price set" from "intentional 0 ₽".
        Plan plan = CreateCoursePlan();
        plan.ClearDomainEvents();

        plan.Publish();

        PlanCourseBoundDomainEvent bound = Assert.IsType<PlanCourseBoundDomainEvent>(plan.DomainEvents.Single());
        Assert.Null(bound.PriceCents);
    }

    [Fact]
    public void Unpublish_active_public_COURSE_raises_Unbound_event()
    {
        Plan plan = CreateCoursePlan();
        plan.Publish();
        plan.ClearDomainEvents();

        plan.Unpublish();

        PlanCourseUnboundDomainEvent unbound = Assert.IsType<PlanCourseUnboundDomainEvent>(plan.DomainEvents.Single());
        Assert.Equal(plan.Id, unbound.PlanId);
        Assert.Equal(plan.FirstCourseId!.Value, unbound.CourseId);
    }

    [Fact]
    public void Archive_active_public_COURSE_raises_Unbound_event()
    {
        Plan plan = CreateCoursePlan();
        plan.Publish();
        plan.ClearDomainEvents();

        UnitResult<Error> result = plan.Archive();

        Assert.True(result.IsSuccess);
        PlanCourseUnboundDomainEvent unbound = Assert.IsType<PlanCourseUnboundDomainEvent>(plan.DomainEvents.Single());
        Assert.Equal(plan.Id, unbound.PlanId);
        Assert.Equal(plan.FirstCourseId!.Value, unbound.CourseId);
    }

    [Fact]
    public void Archive_active_not_public_COURSE_does_not_raise_Unbound_event()
    {
        // Plan was active but never published — never appeared in the catalog,
        // so no consumer holds cached pricing for it. Suppress the Unbound event
        // to keep semantics symmetric with Publish/Unpublish (which only fire on
        // catalog visibility transitions, not on draft-state changes).
        Plan plan = CreateCoursePlan();
        plan.ClearDomainEvents();

        UnitResult<Error> result = plan.Archive();

        Assert.True(result.IsSuccess);
        Assert.Empty(plan.DomainEvents.OfType<PlanCourseUnboundDomainEvent>());
    }

    [Fact]
    public void Archive_FULL_ALL_does_not_raise_Course_events()
    {
        Plan plan = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.FULL_ALL,
            slug: Slug,
            displayName: Name,
            courseIds: [],
            requestedCapabilities: null).Value;
        plan.Publish();
        plan.ClearDomainEvents();

        plan.Archive();

        Assert.Empty(plan.DomainEvents.OfType<PlanCourseUnboundDomainEvent>());
        Assert.Empty(plan.DomainEvents.OfType<PlanCourseBoundDomainEvent>());
    }

    [Fact]
    public void Unarchive_active_public_COURSE_raises_Bound_event()
    {
        Plan plan = CreateCoursePlan();
        plan.Publish();
        plan.Archive();
        plan.ClearDomainEvents();

        plan.Unarchive();
        plan.Publish();

        // Unarchive itself only sets IsActive=true (IsPublic still false), so a
        // subsequent Publish raises Bound — assert at least one Bound event landed.
        Assert.NotEmpty(plan.DomainEvents.OfType<PlanCourseBoundDomainEvent>());
    }

    [Fact]
    public void Publish_already_public_COURSE_is_noop_and_does_not_re_raise_Bound()
    {
        Plan plan = CreateCoursePlan();
        plan.Publish();
        plan.ClearDomainEvents();

        plan.Publish();

        Assert.Empty(plan.DomainEvents);
    }

    [Fact]
    public void Unpublish_already_unpublished_COURSE_is_noop_and_does_not_raise_Unbound()
    {
        Plan plan = CreateCoursePlan();
        plan.ClearDomainEvents();

        plan.Unpublish();

        Assert.Empty(plan.DomainEvents);
    }
}
