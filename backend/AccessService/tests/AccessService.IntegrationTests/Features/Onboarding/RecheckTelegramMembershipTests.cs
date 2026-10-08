using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using System.Net;
using System.Net.Http.Json;
using TelegramBotService.Contracts.Dtos;

namespace AccessService.IntegrationTests.Features.Onboarding;

/// <summary>
///     Endpoint tests for <c>POST /access/onboarding/{planId}/steps/telegram/recheck/</c>
///     (epic #397). Member → completes; unknown/not-member → no-op 200; service down →
///     soft-degrade 200; unauthenticated → 401.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class RecheckTelegramMembershipTests : AccessServiceTestsBase
{
    public RecheckTelegramMembershipTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Member_completes_telegram_step()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid telegramStepId = await TelegramStepIdAsync(planId);

        AuthenticateAs("platform-participant", studentId);
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: true, Status: "member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/telegram/recheck/", content: null);
        resp.EnsureSuccessStatusCode();

        Envelope<RecheckTelegramMembershipResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<RecheckTelegramMembershipResponse>>();
        Assert.NotNull(env);
        Assert.True(env.Result!.Completed);
        Assert.Equal("member", env.Result.Status);

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.Contains(telegramStepId, ob.CompletedStepIds);
        });
    }

    [Fact]
    public async Task Not_member_is_noop_with_200()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        AuthenticateAs("platform-participant", studentId);
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/telegram/recheck/", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<RecheckTelegramMembershipResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<RecheckTelegramMembershipResponse>>();
        Assert.False(env!.Result!.Completed);
        Assert.Equal("not_member", env.Result.Status);

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.Empty(ob.CompletedStepIds);
        });
    }

    [Fact]
    public async Task Unknown_status_is_noop_with_200()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        AuthenticateAs("platform-participant", studentId);
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "unknown");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/telegram/recheck/", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<RecheckTelegramMembershipResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<RecheckTelegramMembershipResponse>>();
        Assert.False(env!.Result!.Completed);
        Assert.Equal("unknown", env.Result.Status);
    }

    [Fact]
    public async Task Telegram_service_down_soft_degrades_with_200()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        AuthenticateAs("platform-participant", studentId);
        Factory.TelegramClient.ShouldFail = true;

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/telegram/recheck/", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<RecheckTelegramMembershipResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<RecheckTelegramMembershipResponse>>();
        Assert.False(env!.Result!.Completed);
        Assert.Equal("unknown", env.Result.Status);
    }

    [Fact]
    public async Task Returns_not_found_when_no_onboarding_row()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid(); // no onboarding seeded

        AuthenticateAs("platform-participant", studentId);
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: true, Status: "member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/telegram/recheck/", content: null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_returns_401()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        RemoveAuthentication();

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/telegram/recheck/", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Already_completed_step_advances_stuck_cursor_to_pending()
    {
        // #596 reproducer: курсор припаркован на УЖЕ завершённом TELEGRAM-шаге (back-nav по
        // степперу через return-to). До фикса recheck отвечал «подтверждено», но курсор не
        // двигался — юзер залипал. Теперь recheck продвигает курсор на первый pending-шаг.
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        Guid telegramStepId = await TelegramStepIdAsync(planId);
        await SeedOnboardingStuckOnCompletedStepAsync(studentId, planId, telegramStepId);

        AuthenticateAs("platform-participant", studentId);
        // Намеренно not_member: на уже завершённом шаге recheck НЕ должен пере-проверять
        // членство — только расцепить залипший курсор.
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/telegram/recheck/", content: null);
        resp.EnsureSuccessStatusCode();

        Envelope<RecheckTelegramMembershipResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<RecheckTelegramMembershipResponse>>();
        Assert.True(env!.Result!.Completed);
        Assert.Equal("member", env.Result.Status);

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

    private async Task<Guid> TelegramStepIdAsync(Guid planId) =>
        await ExecuteInDbAsync(async db => await db.PlanOnboardingSteps
            .Where(s => s.PlanId == planId && s.Type == PlanOnboardingStepType.TELEGRAM)
            .Select(s => s.Id)
            .SingleAsync());

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
