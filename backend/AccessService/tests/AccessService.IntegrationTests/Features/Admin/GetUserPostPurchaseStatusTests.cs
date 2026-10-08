using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Core.Features.Admin;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;

namespace AccessService.IntegrationTests.Features.Admin;

/// <summary>
///     Endpoint tests for <c>GET /access/admin/users/{userId}/post-purchase-status</c> (#444) —
///     консолидированный support/admin снимок: активные grant'ы, статус онбординга, Telegram-
///     членство и диагностики. Заменяет ручную диагностику «купил, но не попал в Telegram».
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetUserPostPurchaseStatusTests : AccessServiceTestsBase
{
    public GetUserPostPurchaseStatusTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Admin_sees_active_grant_pending_telegram_and_diagnostic()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedActiveGrantAsync(studentId, planId);
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        AuthenticateAsAdmin();
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/admin/users/{studentId}/post-purchase-status");
        resp.EnsureSuccessStatusCode();

        Envelope<PostPurchaseStatusResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<PostPurchaseStatusResponse>>();
        Assert.NotNull(env);
        PostPurchaseStatusResponse res = env!.Result!;

        PostPurchaseGrantDto grant = Assert.Single(res.ActiveGrants);
        Assert.Equal(planId, grant.PlanId);

        Assert.NotNull(grant.Onboarding);
        Assert.True(grant.Onboarding!.FlowEnabled);
        Assert.True(grant.Onboarding.Started);
        Assert.False(grant.Onboarding.Completed);
        Assert.True(grant.Onboarding.TelegramStepPending);

        Assert.True(grant.Telegram.ChatBound);
        Assert.False(grant.Telegram.IsMember);
        Assert.Equal("not_member", grant.Telegram.Status);

        Assert.Contains(res.Diagnostics, d => d.Contains("Telegram", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Member_telegram_status_has_no_membership_diagnostic()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedActiveGrantAsync(studentId, planId);
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        AuthenticateAsAdmin();
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: true, Status: "member");

        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/admin/users/{studentId}/post-purchase-status");
        resp.EnsureSuccessStatusCode();

        Envelope<PostPurchaseStatusResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<PostPurchaseStatusResponse>>();
        PostPurchaseGrantDto grant = Assert.Single(env!.Result!.ActiveGrants);

        Assert.True(grant.Telegram.IsMember);
        Assert.Equal("member", grant.Telegram.Status);
        Assert.DoesNotContain(env.Result.Diagnostics, d => d.Contains("Telegram-членство", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Telegram_service_down_reports_unknown_without_500()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedActiveGrantAsync(studentId, planId);
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        AuthenticateAsAdmin();
        Factory.TelegramClient.ShouldFail = true;

        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/admin/users/{studentId}/post-purchase-status");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<PostPurchaseStatusResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<PostPurchaseStatusResponse>>();
        PostPurchaseGrantDto grant = Assert.Single(env!.Result!.ActiveGrants);
        Assert.Equal("unknown", grant.Telegram.Status);
    }

    [Fact]
    public async Task Participant_is_forbidden()
    {
        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);

        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/admin/users/{studentId}/post-purchase-status");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    private async Task SeedActiveGrantAsync(Guid userId, Guid planId)
    {
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(userId, planId, PlanGrantSource.PURCHASE, sourceRef: null);
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedOnboardingAtFirstStepAsync(Guid userId, Guid planId)
    {
        await ExecuteInDbAsync(async db =>
        {
            List<PlanOnboardingStep> steps = await db.PlanOnboardingSteps
                .Where(s => s.PlanId == planId)
                .ToListAsync();
            Guid firstStepId = steps
                .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
                .Select(s => s.Id)
                .First();

            UserPlanOnboarding ob = UserPlanOnboarding.Start(userId, planId, DateTimeOffset.UtcNow);
            ob.SetCurrentStep(firstStepId);
            db.UserPlanOnboardings.Add(ob);
            await db.SaveChangesAsync();
        });
    }

    private async Task<Guid> CreatePlanWithTelegramStepAsync()
    {
        Guid planId = await CreatePlanAsync();
        Factory.TelegramClient.ActiveBindingsByPlanId.Add(planId);
        await EnableOnboardingAsync(planId);
        return planId;
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
