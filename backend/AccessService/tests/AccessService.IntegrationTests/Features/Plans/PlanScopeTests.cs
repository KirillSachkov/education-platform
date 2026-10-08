using AccessService.Domain;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// Pure domain tests for the <see cref="Plan.Scope"/> discriminator (#674). Scope is a derived
/// invariant of <see cref="Plan.OfferType"/> — TRAINER_PRO ⇒ TRAINER, everything else ⇒ PLATFORM —
/// set in the factory and re-derived on <see cref="Plan.UpdateOfferType"/>, so callers can never
/// set it inconsistently. No DB / HTTP.
/// </summary>
public class PlanScopeTests
{
    private static readonly PlanSlug Slug = PlanSlug.Of("scope-plan").Value;
    private static readonly PlanDisplayName Name = PlanDisplayName.Of("Scope").Value;

    [Fact]
    public void Subscription_TRAINER_PRO_plan_gets_TRAINER_scope()
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
        Assert.Equal(PlanOfferType.TRAINER_PRO, result.Value.OfferType);
        Assert.Equal(PlanScope.TRAINER, result.Value.Scope);
    }

    [Fact]
    public void Full_all_plan_gets_PLATFORM_scope()
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(), PlanTier.FULL_ALL, Slug, Name, courseIds: [], requestedCapabilities: null);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanOfferType.FULL_ACCESS, result.Value.OfferType);
        Assert.Equal(PlanScope.PLATFORM, result.Value.Scope);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PlanOfferType.COURSE)]
    [InlineData(PlanOfferType.INTENSIVE)]
    [InlineData(PlanOfferType.MARATHON)]
    public void Course_plan_gets_PLATFORM_scope_for_every_marketing_offer(PlanOfferType? offerType)
    {
        Result<Plan, Error> result = Plan.Create(
            Guid.NewGuid(),
            PlanTier.COURSE,
            Slug,
            Name,
            courseIds: [Guid.NewGuid()],
            requestedCapabilities: null,
            offerType: offerType);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanScope.PLATFORM, result.Value.Scope);
    }

    [Fact]
    public void UpdateOfferType_on_course_plan_keeps_PLATFORM_scope()
    {
        Plan plan = Plan.Create(
            Guid.NewGuid(), PlanTier.COURSE, Slug, Name,
            courseIds: [Guid.NewGuid()], requestedCapabilities: null).Value;

        UnitResult<Error> update = plan.UpdateOfferType(PlanOfferType.INTENSIVE);

        Assert.True(update.IsSuccess);
        Assert.Equal(PlanOfferType.INTENSIVE, plan.OfferType);
        Assert.Equal(PlanScope.PLATFORM, plan.Scope);
    }

    [Fact]
    public void UpdateOfferType_on_subscription_plan_stays_TRAINER_scope()
    {
        Plan plan = Plan.Create(
            Guid.NewGuid(), PlanTier.SUBSCRIPTION, Slug, Name,
            courseIds: [], requestedCapabilities: null,
            term: new PlanTerm(PlanTermKind.RECURRING, 30)).Value;

        // SUBSCRIPTION forces TRAINER_PRO regardless of input → scope stays TRAINER.
        UnitResult<Error> update = plan.UpdateOfferType(PlanOfferType.COURSE);

        Assert.True(update.IsSuccess);
        Assert.Equal(PlanOfferType.TRAINER_PRO, plan.OfferType);
        Assert.Equal(PlanScope.TRAINER, plan.Scope);
    }
}
