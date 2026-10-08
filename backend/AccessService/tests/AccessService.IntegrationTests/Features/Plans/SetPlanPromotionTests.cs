using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Dtos;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class SetPlanPromotionTests : AccessServiceTestsBase
{
    public SetPlanPromotionTests(IntegrationTestsWebFactory factory) : base(factory) { }

    private async Task<Guid> CreatePlanAsync(
        string slug,
        long? priceCents = 990_000,
        string tier = nameof(PlanTier.FULL_ALL))
    {
        CreatePlanRequest request = new(
            Tier: tier,
            Slug: slug,
            DisplayName: "Полный доступ",
            ShortDescription: "Доступ ко всему",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: priceCents,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        create.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await create.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        return envelope!.Result;
    }

    private async Task PublishAsync(Guid planId)
    {
        HttpResponseMessage publish = await AppHttpClient.PostAsync($"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();
    }

    private async Task<PublicPlanDto> GetPublicPlanAsync(Guid planId)
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/plans/public");
        response.EnsureSuccessStatusCode();
        Envelope<IReadOnlyList<PublicPlanDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<PublicPlanDto>>>();
        Assert.NotNull(envelope);
        return envelope!.Result!.Single(p => p.Id == planId);
    }

    [Fact]
    public async Task PUT_promotion_AsOwner_SetsDiscount_PublicDtoReflectsEffectivePrice()
    {
        Guid planId = await CreatePlanAsync("promo-set", priceCents: 990_000);
        await PublishAsync(planId);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SetPromotionRequest request = new(30, now.AddMinutes(-1), now.AddDays(7));

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/promotion/", request);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        PublicPlanDto plan = await GetPublicPlanAsync(planId);
        Assert.True(plan.PromotionActive);
        Assert.Equal(30, plan.DiscountPercent);
        Assert.Equal(693_000, plan.EffectivePriceCents); // 990000 - 30%
        Assert.Equal(990_000, plan.PriceCents);
        Assert.NotNull(plan.DiscountEndsAt);
    }

    [Fact]
    public async Task PUT_promotion_OnPlanWithoutPrice_Returns400()
    {
        // Issue #358: FREE-tier deprecated, тест переключён на FULL_ALL — тот же
        // edge-case «promotion на плане без цены» работает на любом tier.
        Guid planId = await CreatePlanAsync("promo-no-price", priceCents: null, tier: nameof(PlanTier.FULL_ALL));

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SetPromotionRequest request = new(30, now, now.AddDays(7));

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/promotion/", request);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        Envelope? envelope = await put.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "plan.promotion.requires_price", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PUT_promotion_WithInvalidPercent_Returns400()
    {
        Guid planId = await CreatePlanAsync("promo-bad-percent", priceCents: 990_000);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SetPromotionRequest request = new(0, now, now.AddDays(7));

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/promotion/", request);

        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task PUT_promotion_ByNonOwner_Returns403()
    {
        Guid planId = await CreatePlanAsync("promo-owner", priceCents: 990_000);

        // Switch to another author — they have plans.manage but not ownership.
        AuthenticateAs("platform-author", Guid.NewGuid());

        DateTimeOffset now = DateTimeOffset.UtcNow;
        SetPromotionRequest request = new(30, now, now.AddDays(7));

        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/promotion/", request);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    [Fact]
    public async Task DELETE_promotion_ClearsDiscount_PublicDtoRevertsToListPrice()
    {
        Guid planId = await CreatePlanAsync("promo-clear", priceCents: 990_000);
        await PublishAsync(planId);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/promotion/", new SetPromotionRequest(30, now.AddMinutes(-1), now.AddDays(7)));

        HttpResponseMessage delete = await AppHttpClient.DeleteAsync($"/access/plans/{planId}/promotion/");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        PublicPlanDto plan = await GetPublicPlanAsync(planId);
        Assert.False(plan.PromotionActive);
        Assert.Null(plan.DiscountPercent);
        Assert.Equal(990_000, plan.EffectivePriceCents);
    }

    [Fact]
    public async Task ExpiredPromotion_SeededInPast_PublicDtoIsInactive()
    {
        Guid planId = await CreatePlanAsync("promo-expired", priceCents: 990_000);
        await PublishAsync(planId);

        // Seed a window entirely in the past directly — bypasses the future-window
        // guard to exercise the read-time inactivity path (auto-revert without a job).
        DateTimeOffset past = DateTimeOffset.UtcNow.AddDays(-30);
        await ExecuteInDbAsync(async db =>
        {
            await db.Plans
                .Where(p => p.Id == planId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(p => p.DiscountPercent, 30)
                    .SetProperty(p => p.DiscountStartsAt, past)
                    .SetProperty(p => p.DiscountEndsAt, past.AddDays(7)));
        });

        PublicPlanDto plan = await GetPublicPlanAsync(planId);
        Assert.False(plan.PromotionActive);
        Assert.Equal(990_000, plan.EffectivePriceCents);
    }
}
