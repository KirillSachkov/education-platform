using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using AccessService.Infrastructure.Postgres;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using System.Net;
using System.Net.Http.Json;

namespace AccessService.IntegrationTests.Features.Onboarding;

/// <summary>
///     Endpoint tests for <c>POST /access/plans/{planId}/onboarding-flow/reset-all/</c>
///     (epic #397). Seed N onboardings in mixed states → reset-all → all back to first
///     step with completed_at null; ownership enforced (non-owner 403, admin ok);
///     ResetCount correct; disabled/no-steps flow → ResetCount 0.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class ResetAllOnboardingsTests : AccessServiceTestsBase
{
    public ResetAllOnboardingsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Resets_all_onboardings_to_first_step()
    {
        // Owner = DefaultUserId (default platform-author identity).
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);
        Guid firstStepId = await FirstStepIdAsync(planId);

        // 3 onboardings in different states.
        Guid u1 = Guid.NewGuid();
        Guid u2 = Guid.NewGuid();
        Guid u3 = Guid.NewGuid();
        await SeedCompletedOnboardingAsync(u1, planId);          // fully completed
        await SeedPartiallyProgressedOnboardingAsync(u2, planId); // some skipped/completed
        await SeedOnboardingAtFirstStepAsync(u3, planId);         // fresh

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/onboarding-flow/reset-all/", content: null);
        resp.EnsureSuccessStatusCode();

        Envelope<ResetAllOnboardingsResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<ResetAllOnboardingsResponse>>();
        Assert.NotNull(env);
        Assert.Equal(3, env.Result!.ResetCount);

        await ExecuteInDbAsync(async db =>
        {
            List<UserPlanOnboarding> all = await db.UserPlanOnboardings
                .Where(o => o.PlanId == planId)
                .ToListAsync();
            Assert.Equal(3, all.Count);
            foreach (UserPlanOnboarding ob in all)
            {
                Assert.Null(ob.CompletedAt);
                Assert.Empty(ob.CompletedStepIds);
                Assert.Empty(ob.SkippedStepIds);
                Assert.Equal(firstStepId, ob.CurrentStepId);
            }
        });
    }

    [Fact]
    public async Task Returns_zero_when_no_onboardings()
    {
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/onboarding-flow/reset-all/", content: null);
        resp.EnsureSuccessStatusCode();

        Envelope<ResetAllOnboardingsResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<ResetAllOnboardingsResponse>>();
        Assert.Equal(0, env!.Result!.ResetCount);
    }

    [Fact]
    public async Task Returns_zero_when_flow_disabled_without_touching_rows()
    {
        Guid planId = await CreatePlanAsync();
        await EnableOnboardingAsync(planId);

        Guid u1 = Guid.NewGuid();
        await SeedCompletedOnboardingAsync(u1, planId);

        // Disable the flow — reset-all must not strand the user at a non-existent step.
        HttpResponseMessage disable = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: false));
        disable.EnsureSuccessStatusCode();

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/onboarding-flow/reset-all/", content: null);
        resp.EnsureSuccessStatusCode();

        Envelope<ResetAllOnboardingsResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<ResetAllOnboardingsResponse>>();
        Assert.Equal(0, env!.Result!.ResetCount);

        // Row untouched — still completed.
        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == u1 && o.PlanId == planId);
            Assert.NotNull(ob.CompletedAt);
        });
    }

    [Fact]
    public async Task Non_owner_author_is_forbidden()
    {
        Guid planId = await CreatePlanAsync(); // owned by DefaultUserId
        await EnableOnboardingAsync(planId);

        Guid otherAuthorId = Guid.NewGuid();
        AuthenticateAs("platform-author", otherAuthorId);

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/onboarding-flow/reset-all/", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Admin_can_reset_any_plan()
    {
        Guid planId = await CreatePlanAsync(); // owned by DefaultUserId
        await EnableOnboardingAsync(planId);
        await SeedOnboardingAtFirstStepAsync(Guid.NewGuid(), planId);

        AuthenticateAsAdmin(Guid.NewGuid()); // admin who is NOT the owner

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/onboarding-flow/reset-all/", content: null);
        resp.EnsureSuccessStatusCode();

        Envelope<ResetAllOnboardingsResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<ResetAllOnboardingsResponse>>();
        Assert.Equal(1, env!.Result!.ResetCount);
    }

    private async Task<Guid> FirstStepIdAsync(Guid planId) =>
        await ExecuteInDbAsync(async db =>
        {
            List<PlanOnboardingStep> steps = await db.PlanOnboardingSteps
                .Where(s => s.PlanId == planId)
                .ToListAsync();
            return steps
                .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
                .Select(s => s.Id)
                .First();
        });

    private async Task SeedOnboardingAtFirstStepAsync(Guid userId, Guid planId)
    {
        await ExecuteInDbAsync(async db =>
        {
            Guid firstStepId = await FirstStepIdScopedAsync(db, planId);
            UserPlanOnboarding ob = UserPlanOnboarding.Start(userId, planId, DateTimeOffset.UtcNow);
            ob.SetCurrentStep(firstStepId);
            db.UserPlanOnboardings.Add(ob);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedPartiallyProgressedOnboardingAsync(Guid userId, Guid planId)
    {
        await ExecuteInDbAsync(async db =>
        {
            List<Guid> stepIds = await StepIdsScopedAsync(db, planId);
            UserPlanOnboarding ob = UserPlanOnboarding.Start(userId, planId, DateTimeOffset.UtcNow);
            ob.CompleteStep(stepIds[0]);
            if (stepIds.Count > 1) ob.SkipStep(stepIds[1]);
            ob.AdvanceTo(stepIds);
            db.UserPlanOnboardings.Add(ob);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedCompletedOnboardingAsync(Guid userId, Guid planId)
    {
        await ExecuteInDbAsync(async db =>
        {
            List<Guid> stepIds = await StepIdsScopedAsync(db, planId);
            UserPlanOnboarding ob = UserPlanOnboarding.Start(userId, planId, DateTimeOffset.UtcNow);
            foreach (Guid id in stepIds) ob.CompleteStep(id);
            ob.Complete(stepIds, DateTimeOffset.UtcNow);
            db.UserPlanOnboardings.Add(ob);
            await db.SaveChangesAsync();
        });
    }

    private static async Task<Guid> FirstStepIdScopedAsync(
        AccessServiceDbContext db, Guid planId)
    {
        List<PlanOnboardingStep> steps = await db.PlanOnboardingSteps
            .Where(s => s.PlanId == planId)
            .ToListAsync();
        return steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id)
            .First();
    }

    private static async Task<List<Guid>> StepIdsScopedAsync(
        AccessServiceDbContext db, Guid planId)
    {
        List<PlanOnboardingStep> steps = await db.PlanOnboardingSteps
            .Where(s => s.PlanId == planId)
            .ToListAsync();
        return steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id)
            .ToList();
    }

    private async Task<Guid> CreatePlanAsync()
    {
        CreatePlanRequest req = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: $"plan-{Guid.NewGuid():N}",
            DisplayName: "Test Plan",
            ShortDescription: "desc",
            LongDescription: "long",
            CoverFileId: null,
            Features: new[] { "f1" },
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync("/access/plans/", req);
        resp.EnsureSuccessStatusCode();
        Envelope<Guid>? env = await resp.Content.ReadFromJsonAsync<Envelope<Guid>>();
        return env!.Result;
    }

    private async Task EnableOnboardingAsync(Guid planId)
    {
        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));
        resp.EnsureSuccessStatusCode();
    }
}
