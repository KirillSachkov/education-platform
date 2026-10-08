using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;

namespace AccessService.IntegrationTests.Features.Admin;

/// <summary>
///     Endpoint tests for
///     <c>POST /access/admin/users/{userId}/plans/{planId}/telegram/recheck/</c> (#444) —
///     admin/support variant of the user-facing TELEGRAM-step recheck. Covers: member →
///     step completed, non-member → no-op, TelegramBotService soft-degrade → 200 unknown,
///     and role enforcement (admin/moderator allowed, author/student forbidden).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class RecheckTelegramMembershipForUserTests : AccessServiceTestsBase
{
    public RecheckTelegramMembershipForUserTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Member_completes_pending_telegram_step()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedActiveGrantAsync(studentId, planId);
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        AuthenticateAsAdmin();
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: true, Status: "member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/telegram/recheck/", content: null);
        resp.EnsureSuccessStatusCode();

        RecheckTelegramMembershipResponse res = await ReadResultAsync(resp);
        Assert.True(res.Completed);
        Assert.Equal("member", res.Status);

        Guid telegramStepId = await GetTelegramStepIdAsync(planId);
        await AssertStepCompletedAsync(studentId, planId, telegramStepId, expected: true);
    }

    [Fact]
    public async Task Non_member_leaves_step_pending()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedActiveGrantAsync(studentId, planId);
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        AuthenticateAsAdmin();
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/telegram/recheck/", content: null);
        resp.EnsureSuccessStatusCode();

        RecheckTelegramMembershipResponse res = await ReadResultAsync(resp);
        Assert.False(res.Completed);
        Assert.Equal("not_member", res.Status);

        Guid telegramStepId = await GetTelegramStepIdAsync(planId);
        await AssertStepCompletedAsync(studentId, planId, telegramStepId, expected: false);
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

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/telegram/recheck/", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        RecheckTelegramMembershipResponse res = await ReadResultAsync(resp);
        Assert.False(res.Completed);
        Assert.Equal("unknown", res.Status);

        Guid telegramStepId = await GetTelegramStepIdAsync(planId);
        await AssertStepCompletedAsync(studentId, planId, telegramStepId, expected: false);
    }

    [Fact]
    public async Task Moderator_is_allowed()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedActiveGrantAsync(studentId, planId);
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        AuthenticateAs("platform-moderator", Guid.NewGuid());
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: true, Status: "member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/telegram/recheck/", content: null);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        RecheckTelegramMembershipResponse res = await ReadResultAsync(resp);
        Assert.True(res.Completed);
    }

    [Fact]
    public async Task Author_is_forbidden()
    {
        Guid studentId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        AuthenticateAs("platform-author", Guid.NewGuid());

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/telegram/recheck/", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Participant_is_forbidden()
    {
        Guid studentId = Guid.NewGuid();
        Guid planId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/telegram/recheck/", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Missing_onboarding_returns_404()
    {
        // Grant issued but the onboarding row was never created (flow disabled at grant time,
        // or PlanGrantCreatedOnboardingHandler hasn't run) → support gets an explicit 404,
        // not an opaque 200, so they know there's nothing to push past.
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedActiveGrantAsync(studentId, planId);
        // intentionally NO SeedOnboardingAtFirstStepAsync

        AuthenticateAsAdmin();
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: true, Status: "member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/telegram/recheck/", content: null);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Already_completed_step_advances_stuck_cursor()
    {
        // #596 reproducer (support path): курсор юзера припаркован на УЖЕ завершённом
        // TELEGRAM-шаге (back-nav). До фикса admin-recheck отвечал completed=true без AdvanceTo —
        // саппорт не мог расцепить залипшего юзера. Теперь продвигает курсор на первый pending.
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        Guid telegramStepId = await GetTelegramStepIdAsync(planId);
        await SeedOnboardingStuckOnCompletedStepAsync(studentId, planId, telegramStepId);

        AuthenticateAsAdmin();
        // not_member: на уже завершённом шаге TBS дёргать не должны — только расцепить курсор.
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/telegram/recheck/", content: null);
        resp.EnsureSuccessStatusCode();

        RecheckTelegramMembershipResponse res = await ReadResultAsync(resp);
        Assert.True(res.Completed);
        Assert.Equal("member", res.Status);

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.NotNull(ob.CurrentStepId);
            Assert.NotEqual(telegramStepId, ob.CurrentStepId);                    // съехал с завершённого
            Assert.DoesNotContain(ob.CurrentStepId!.Value, ob.CompletedStepIds);  // на pending-шаг
        });
    }

    private async Task SeedOnboardingStuckOnCompletedStepAsync(Guid userId, Guid planId, Guid completedStepId)
    {
        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = UserPlanOnboarding.Start(userId, planId, DateTimeOffset.UtcNow);
            ob.CompleteStep(completedStepId);
            ob.SetCurrentStep(completedStepId); // курсор припаркован на завершённом шаге (back-nav)
            db.UserPlanOnboardings.Add(ob);
            await db.SaveChangesAsync();
        });
    }

    private static async Task<RecheckTelegramMembershipResponse> ReadResultAsync(HttpResponseMessage resp)
    {
        Envelope<RecheckTelegramMembershipResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<RecheckTelegramMembershipResponse>>();
        Assert.NotNull(env);
        return env!.Result!;
    }

    private async Task<Guid> GetTelegramStepIdAsync(Guid planId) =>
        await ExecuteInDbAsync(async db =>
        {
            PlanOnboardingStep step = await db.PlanOnboardingSteps
                .Where(s => s.PlanId == planId && s.Type == PlanOnboardingStepType.TELEGRAM)
                .FirstAsync();
            return step.Id;
        });

    private async Task AssertStepCompletedAsync(Guid userId, Guid planId, Guid stepId, bool expected)
    {
        bool completed = await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding onboarding = await db.UserPlanOnboardings
                .FirstAsync(o => o.UserId == userId && o.PlanId == planId);
            return onboarding.CompletedStepIds.Contains(stepId);
        });
        Assert.Equal(expected, completed);
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
