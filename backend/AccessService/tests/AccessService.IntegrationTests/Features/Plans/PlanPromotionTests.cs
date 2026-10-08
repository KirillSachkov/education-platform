using AccessService.Domain;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// Pure domain tests for the plan promotion (акция): <see cref="Plan.SetPromotion"/>
/// validation + the read-time <see cref="Plan.IsPromotionActive"/> /
/// <see cref="Plan.EffectivePriceCents"/> window logic (auto-revert without a job).
/// </summary>
public class PlanPromotionTests
{
    private static readonly PlanSlug Slug = PlanSlug.Of("promo-plan").Value;
    private static readonly PlanDisplayName Name = PlanDisplayName.Of("Promo").Value;
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static Plan PricedPlan(long priceCents = 100_000)
    {
        Plan plan = Plan.Create(Guid.NewGuid(), PlanTier.FULL_ALL, Slug, Name, [], null).Value;
        plan.UpdatePrice(priceCents, "RUB");
        return plan;
    }

    [Fact]
    public void SetPromotion_OnValidWindow_Succeeds()
    {
        Plan plan = PricedPlan();

        UnitResult<Error> result = plan.SetPromotion(30, T0, T0.AddDays(7), now: T0.AddHours(-1));

        Assert.True(result.IsSuccess);
        Assert.Equal(30, plan.DiscountPercent);
        Assert.Equal(T0, plan.DiscountStartsAt);
        Assert.Equal(T0.AddDays(7), plan.DiscountEndsAt);
    }

    [Fact]
    public void SetPromotion_OnPlanWithoutPrice_ReturnsRequiresPrice()
    {
        Plan plan = Plan.Create(Guid.NewGuid(), PlanTier.FULL_ALL, Slug, Name, [], null).Value;

        UnitResult<Error> result = plan.SetPromotion(30, T0, T0.AddDays(7), now: T0.AddHours(-1));

        Assert.True(result.IsFailure);
        Assert.Equal("plan.promotion.requires_price", result.Error.Messages[0].Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(-5)]
    public void SetPromotion_WithPercentOutOfRange_ReturnsPercentInvalid(int percent)
    {
        Plan plan = PricedPlan();

        UnitResult<Error> result = plan.SetPromotion(percent, T0, T0.AddDays(7), now: T0.AddHours(-1));

        Assert.True(result.IsFailure);
        Assert.Equal("plan.promotion.percent_invalid", result.Error.Messages[0].Code);
    }

    [Fact]
    public void SetPromotion_WithEndsBeforeStarts_ReturnsWindowInvalid()
    {
        Plan plan = PricedPlan();

        UnitResult<Error> result = plan.SetPromotion(30, T0.AddDays(7), T0, now: T0.AddHours(-1));

        Assert.True(result.IsFailure);
        Assert.Equal("plan.promotion.window_invalid", result.Error.Messages[0].Code);
    }

    [Fact]
    public void SetPromotion_WithEndsInPast_ReturnsWindowInPast()
    {
        Plan plan = PricedPlan();

        UnitResult<Error> result = plan.SetPromotion(30, T0, T0.AddDays(1), now: T0.AddDays(2));

        Assert.True(result.IsFailure);
        Assert.Equal("plan.promotion.window_in_past", result.Error.Messages[0].Code);
    }

    [Fact]
    public void NoPromotion_IsInactive_EffectiveEqualsListPrice()
    {
        Plan plan = PricedPlan(100_000);

        Assert.False(plan.IsPromotionActive(T0));
        Assert.Equal(100_000, plan.EffectivePriceCents(T0));
    }

    [Fact]
    public void Promotion_BeforeStart_IsScheduled_NotActive()
    {
        Plan plan = PricedPlan(100_000);
        plan.SetPromotion(30, T0, T0.AddDays(7), now: T0.AddHours(-2));

        Assert.False(plan.IsPromotionActive(T0.AddHours(-1)));
        Assert.Equal(100_000, plan.EffectivePriceCents(T0.AddHours(-1)));
    }

    [Fact]
    public void Promotion_WithinWindow_IsActive_AppliesDiscount()
    {
        Plan plan = PricedPlan(100_000);
        plan.SetPromotion(30, T0, T0.AddDays(7), now: T0.AddHours(-1));

        Assert.True(plan.IsPromotionActive(T0.AddDays(1)));
        Assert.Equal(70_000, plan.EffectivePriceCents(T0.AddDays(1)));
    }

    [Fact]
    public void Promotion_AfterEnd_RevertsToListPrice_WithoutJob()
    {
        Plan plan = PricedPlan(100_000);
        plan.SetPromotion(30, T0, T0.AddDays(7), now: T0.AddHours(-1));

        // now past endsAt — read-time computation reverts the price automatically.
        Assert.False(plan.IsPromotionActive(T0.AddDays(8)));
        Assert.Equal(100_000, plan.EffectivePriceCents(T0.AddDays(8)));
    }

    [Fact]
    public void EndsAt_IsExclusive_AtExactEnd_NotActive()
    {
        Plan plan = PricedPlan(100_000);
        plan.SetPromotion(30, T0, T0.AddDays(7), now: T0.AddHours(-1));

        Assert.False(plan.IsPromotionActive(T0.AddDays(7)));
        Assert.Equal(100_000, plan.EffectivePriceCents(T0.AddDays(7)));
    }

    [Fact]
    public void UpdatePrice_ToNull_ClearsPromotion()
    {
        Plan plan = PricedPlan(100_000);
        plan.SetPromotion(30, T0, T0.AddDays(7), now: T0.AddHours(-1));

        plan.UpdatePrice(null, "RUB");

        Assert.Null(plan.DiscountPercent);
        Assert.Null(plan.DiscountStartsAt);
        Assert.Null(plan.DiscountEndsAt);
        Assert.False(plan.IsPromotionActive(T0.AddDays(1)));
    }

    [Fact]
    public void ClearPromotion_RemovesAllFields()
    {
        Plan plan = PricedPlan(100_000);
        plan.SetPromotion(30, T0, T0.AddDays(7), now: T0.AddHours(-1));

        plan.ClearPromotion();

        Assert.Null(plan.DiscountPercent);
        Assert.Null(plan.DiscountStartsAt);
        Assert.Null(plan.DiscountEndsAt);
    }
}
