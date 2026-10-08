using AccessService.Domain;
using AccessService.Domain.Events;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// Pure domain tests for the <see cref="Plan"/> factory rules. No DB / HTTP — just
/// asserts on <see cref="Plan.Create"/> output. Tier-first contract (Phase 1.2/1.3).
/// </summary>
public class PlanFactoryTests
{
    private static readonly PlanSlug Slug = PlanSlug.Of("test-plan").Value;
    private static readonly PlanDisplayName Name = PlanDisplayName.Of("Test").Value;

    [Fact]
    public void Create_FULL_ALL_with_courses_fails()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.FULL_ALL,
            Slug,
            Name,
            courseIds: [Guid.NewGuid()],
            requestedCapabilities: null);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.course_id.forbidden", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_LEARN_ALL_is_deprecated()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.LEARN_ALL,
            Slug,
            Name,
            courseIds: [],
            requestedCapabilities: null);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.learn_all.deprecated", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_COURSE_with_empty_list_fails()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.COURSE,
            Slug,
            Name,
            courseIds: [],
            requestedCapabilities: null);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.course_id.required", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_FULL_ALL_succeeds_with_FULL_caps_and_includes_future()
    {
        Guid authorId = Guid.NewGuid();

        Result<Plan, Error> result = Plan.Create(
            authorId,
            PlanTier.FULL_ALL,
            Slug,
            Name,[],
            requestedCapabilities: null);

        Assert.True(result.IsSuccess);
        Plan plan = result.Value;
        Assert.True(plan.IncludesFutureContent);
        Assert.Null(plan.FirstCourseId);
        Assert.Equal(authorId, plan.AuthorId);
        Assert.Equal(PlanTier.FULL_ALL, plan.Tier);
        Assert.Equal(PlanCapabilities.FULL, plan.Capabilities);
        Assert.Single(plan.DomainEvents);
        Assert.IsType<PlanCreatedDomainEvent>(plan.DomainEvents[0]);
    }

    [Fact]
    public void Create_FREE_returns_deprecated_error()
    {
        // Issue #358: FREE-tier creation is blocked at the factory level
        // (mirrors the already-deprecated LEARN_ALL). Existing FREE plans
        // в БД остаются для legacy-чтения, но новые создавать нельзя.
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.FREE,
            Slug,
            Name,
            courseIds: [],
            requestedCapabilities: null);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.free.deprecated", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_COURSE_with_default_caps_uses_VIEW_MATERIALS_plus_SUBMIT_ISSUES()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.COURSE,
            Slug,
            Name,
            courseIds: [Guid.NewGuid()],
            requestedCapabilities: null);

        Assert.True(result.IsSuccess);
        Plan plan = result.Value;
        Assert.Equal(PlanTier.COURSE, plan.Tier);
        Assert.Equal(
            PlanCapabilities.VIEW_MATERIALS | PlanCapabilities.SUBMIT_ISSUES,
            plan.Capabilities);
    }

    [Fact]
    public void Create_COURSE_with_explicit_caps_honors_them()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.COURSE,
            Slug,
            Name,
            courseIds: [Guid.NewGuid()],
            requestedCapabilities: ["VIEW_MATERIALS", "SUBMIT_ISSUES", "CODE_REVIEW"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            PlanCapabilities.VIEW_MATERIALS | PlanCapabilities.SUBMIT_ISSUES | PlanCapabilities.CODE_REVIEW,
            result.Value.Capabilities);
    }

    [Fact]
    public void Create_SUBSCRIPTION_without_recurring_term_fails()
    {
        // #614: SUBSCRIPTION без периодического term'а (по умолчанию Lifetime) отвергается —
        // нечего автопродлевать.
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.SUBSCRIPTION,
            Slug,
            Name,
            courseIds: [],
            requestedCapabilities: null);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.subscription.requires_recurring_term", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_SUBSCRIPTION_with_lifetime_term_fails()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.SUBSCRIPTION,
            Slug,
            Name,
            courseIds: [],
            requestedCapabilities: null,
            term: PlanTerm.Lifetime);

        Assert.True(result.IsFailure);
        Assert.Equal("plan.subscription.requires_recurring_term", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_SUBSCRIPTION_with_non_positive_interval_fails()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.SUBSCRIPTION,
            Slug,
            Name,
            courseIds: [],
            requestedCapabilities: null,
            term: new PlanTerm(PlanTermKind.RECURRING, 0));

        Assert.True(result.IsFailure);
        Assert.Equal("plan.subscription.requires_recurring_term", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_SUBSCRIPTION_with_courses_fails()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.SUBSCRIPTION,
            Slug,
            Name,
            courseIds: [Guid.NewGuid()],
            requestedCapabilities: null,
            term: new PlanTerm(PlanTermKind.RECURRING, 30));

        Assert.True(result.IsFailure);
        Assert.Equal("plan.course_id.forbidden", result.Error.Messages[0].Code);
    }

    [Fact]
    public void Create_SUBSCRIPTION_with_recurring_term_succeeds_with_default_TRAINER_PRO()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.SUBSCRIPTION,
            Slug,
            Name,
            courseIds: [],
            requestedCapabilities: null,
            term: new PlanTerm(PlanTermKind.RECURRING, 30));

        Assert.True(result.IsSuccess);
        Plan plan = result.Value;
        Assert.Equal(PlanTier.SUBSCRIPTION, plan.Tier);
        Assert.Equal(PlanCapabilities.TRAINER_PRO, plan.Capabilities);
        Assert.False(plan.IncludesFutureContent);
        Assert.Empty(plan.Courses);
        Assert.Equal(PlanTermKind.RECURRING, plan.Term.Kind);
        Assert.Equal(30, plan.Term.RecurringIntervalDays);
    }

    [Fact]
    public void Create_SUBSCRIPTION_honors_explicit_capabilities()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.SUBSCRIPTION,
            Slug,
            Name,
            courseIds: [],
            requestedCapabilities: ["TRAINER_PRO", "VIEW_MATERIALS"],
            term: new PlanTerm(PlanTermKind.RECURRING, 7));

        Assert.True(result.IsSuccess);
        Assert.Equal(
            PlanCapabilities.TRAINER_PRO | PlanCapabilities.VIEW_MATERIALS,
            result.Value.Capabilities);
    }

    [Fact]
    public void UpdateCapabilities_on_FULL_ALL_keeps_FULL()
    {
        Plan plan = Plan.Create(Guid.NewGuid(), PlanTier.FULL_ALL, Slug, Name, courseIds: [], null).Value;
        plan.UpdateCapabilities(PlanCapabilities.LEARN_ONLY);
        Assert.Equal(PlanCapabilities.FULL, plan.Capabilities);
    }

    [Fact]
    public void UpdateCapabilities_on_COURSE_applies()
    {
        Plan plan = Plan.Create(
            Guid.NewGuid(), PlanTier.COURSE, Slug, Name,
            courseIds: [Guid.NewGuid()], null).Value;
        plan.UpdateCapabilities(PlanCapabilities.VIEW_MATERIALS | PlanCapabilities.SUBMIT_ISSUES | PlanCapabilities.LIVE_CALLS);
        Assert.Equal(
            PlanCapabilities.VIEW_MATERIALS | PlanCapabilities.SUBMIT_ISSUES | PlanCapabilities.LIVE_CALLS,
            plan.Capabilities);
    }
}
