using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// Verifies the <c>scope</c> column (#674): the domain writes the discriminator end-to-end through
/// EF → DB → read, and the <c>AddPlanScope</c> migration's backfill statement flips legacy
/// TRAINER_PRO rows to TRAINER while leaving everything else PLATFORM.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class PlanScopePersistenceTests : AccessServiceTestsBase
{
    public PlanScopePersistenceTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Created_subscription_plan_persists_TRAINER_scope()
    {
        Guid planId = await CreateSubscriptionPlanAsync("scope-sub", priceCents: 49_000, intervalDays: 30);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == planId);
            Assert.Equal(PlanScope.TRAINER, plan.Scope);
        });
    }

    [Fact]
    public async Task Created_full_all_plan_persists_PLATFORM_scope()
    {
        Guid planId = await CreateFullAllPlanAsync("scope-full");

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == planId);
            Assert.Equal(PlanScope.PLATFORM, plan.Scope);
        });
    }

    [Fact]
    public async Task Migration_backfill_sql_flips_TRAINER_PRO_rows_only()
    {
        // Simulate the pre-migration world: both rows exist but every scope is still 'PLATFORM'
        // (the column default), then run the exact UPDATE the AddPlanScope migration applies.
        Guid trainerPlanId = await CreateSubscriptionPlanAsync("backfill-sub", priceCents: 49_000, intervalDays: 30);
        Guid platformPlanId = await CreateFullAllPlanAsync("backfill-full");

        await ExecuteInDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync("UPDATE access.plans SET scope = 'PLATFORM';");
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE access.plans SET scope = 'TRAINER' WHERE offer_type = 'TRAINER_PRO';");
        });

        await ExecuteInDbAsync(async db =>
        {
            Plan trainer = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == trainerPlanId);
            Plan platform = await db.Plans.AsNoTracking().SingleAsync(p => p.Id == platformPlanId);
            Assert.Equal(PlanScope.TRAINER, trainer.Scope);
            Assert.Equal(PlanScope.PLATFORM, platform.Scope);
        });
    }

    private async Task<Guid> CreateSubscriptionPlanAsync(string slug, int priceCents, int intervalDays)
    {
        AuthenticateAs("platform-author");
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.SUBSCRIPTION),
            Slug: slug,
            DisplayName: "Подписка",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: priceCents,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0,
            RecurringIntervalDays: intervalDays);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;
    }

    private async Task<Guid> CreateFullAllPlanAsync(string slug)
    {
        AuthenticateAs("platform-author");
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: "Полный доступ",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 990_000,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;
    }
}
