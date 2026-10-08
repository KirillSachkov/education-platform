using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using AccessService.Domain.Onboarding;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using TelegramBotService.Contracts.Dtos;

namespace AccessService.IntegrationTests.Features.Onboarding;

/// <summary>
///     Server-side verification of mandatory onboarding steps on
///     <c>POST /access/onboarding/{planId}/steps/{stepId}/complete/</c> (#444). Before this,
///     "Далее" completed any step without proof. Now TELEGRAM requires confirmed chat membership;
///     non-verifiable steps (NOTIFICATIONS / MARKDOWN) still complete freely.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class CompleteOnboardingStepVerificationTests : AccessServiceTestsBase
{
    public CompleteOnboardingStepVerificationTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Telegram_step_complete_rejected_when_not_member()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid telegramStepId = await StepIdAsync(planId, PlanOnboardingStepType.TELEGRAM);

        AuthenticateAs("platform-participant", studentId);
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{telegramStepId}/complete/", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("onboarding.telegram.membership.required", body, StringComparison.Ordinal);

        await AssertStepCompletionAsync(studentId, planId, telegramStepId, completed: false);
    }

    [Fact]
    public async Task Telegram_step_complete_rejected_when_service_unknown()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid telegramStepId = await StepIdAsync(planId, PlanOnboardingStepType.TELEGRAM);

        AuthenticateAs("platform-participant", studentId);
        Factory.TelegramClient.ShouldFail = true; // can't confirm → must not complete

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{telegramStepId}/complete/", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertStepCompletionAsync(studentId, planId, telegramStepId, completed: false);
    }

    [Fact]
    public async Task Telegram_step_complete_succeeds_when_member()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid telegramStepId = await StepIdAsync(planId, PlanOnboardingStepType.TELEGRAM);

        AuthenticateAs("platform-participant", studentId);
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: true, Status: "member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{telegramStepId}/complete/", content: null);
        resp.EnsureSuccessStatusCode();

        await AssertStepCompletionAsync(studentId, planId, telegramStepId, completed: true);
    }

    [Fact]
    public async Task Notifications_step_completes_without_verification()
    {
        Guid planId = await CreatePlanWithTelegramStepAsync(); // onboarding enabled → NOTIFICATIONS step exists
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid notificationsStepId = await StepIdAsync(planId, PlanOnboardingStepType.NOTIFICATIONS);

        AuthenticateAs("platform-participant", studentId);
        // not_member would block a TELEGRAM step — must NOT affect a NOTIFICATIONS step.
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{notificationsStepId}/complete/", content: null);
        resp.EnsureSuccessStatusCode();

        await AssertStepCompletionAsync(studentId, planId, notificationsStepId, completed: true);
    }

    [Fact]
    public async Task Github_step_complete_succeeds_when_user_is_org_member()
    {
        const string org = "sachkovtech";
        Guid planId = await CreatePlanWithGithubStepAsync(org);
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid githubStepId = await StepIdAsync(planId, PlanOnboardingStepType.GITHUB);

        AuthenticateAs("platform-participant", studentId);
        // No invitation record — user is already a member of the org (joined directly / added
        // manually). The green card shows "вы состоите в org" from this same AuthService source,
        // so "Далее" must complete the step (#448), not demand a non-existent invitation.
        Factory.AuthClient.UserIdsByGithubOrg[org] = [studentId];

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{githubStepId}/complete/", content: null);
        resp.EnsureSuccessStatusCode();

        await AssertStepCompletionAsync(studentId, planId, githubStepId, completed: true);
    }

    [Fact]
    public async Task Github_step_complete_rejected_when_not_member_and_no_invitation()
    {
        const string org = "sachkovtech";
        Guid planId = await CreatePlanWithGithubStepAsync(org);
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid githubStepId = await StepIdAsync(planId, PlanOnboardingStepType.GITHUB);

        AuthenticateAs("platform-participant", studentId);
        // Not a member (AuthClient empty for the org) and no invitation → cannot confirm.

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{githubStepId}/complete/", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        string body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("onboarding.github.membership.required", body, StringComparison.Ordinal);

        await AssertStepCompletionAsync(studentId, planId, githubStepId, completed: false);
    }

    [Fact]
    public async Task Github_step_complete_succeeds_when_invitation_accepted()
    {
        const string org = "sachkovtech";
        Guid planId = await CreatePlanWithGithubStepAsync(org);
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid githubStepId = await StepIdAsync(planId, PlanOnboardingStepType.GITHUB);
        await SeedGithubInvitationAsync(
            GithubOrgInvitation.CreateAlreadyMember(planId, studentId, "octocat", org, DateTimeOffset.UtcNow));

        AuthenticateAs("platform-participant", studentId);
        // AuthClient empty for the org → proves the ACCEPTED-invitation fast-path still completes
        // even when the org-membership lookup can't confirm (user accepted via the invite flow).

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{githubStepId}/complete/", content: null);
        resp.EnsureSuccessStatusCode();

        await AssertStepCompletionAsync(studentId, planId, githubStepId, completed: true);
    }

    [Fact]
    public async Task Github_step_complete_succeeds_via_live_membership_check()
    {
        // #501: invitation FAILED (давняя no_installation строка), кэш orgs из AuthService
        // пуст (юзер добавлен в org вручную и не релогинился) — но live-проверка через
        // GitHub App автора подтверждает членство → «Далее» должно пройти.
        const string org = "sachkovtech";
        Guid planId = await CreatePlanWithGithubStepAsync(org);
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid githubStepId = await StepIdAsync(planId, PlanOnboardingStepType.GITHUB);
        await SeedGithubInvitationAsync(
            GithubOrgInvitation.CreateFailed(planId, studentId, "haxnted", org, "no_installation", DateTimeOffset.UtcNow.AddDays(-30)));
        await ExecuteInDbAsync(async db =>
        {
            db.AuthorGithubInstallations.Add(AuthorGithubInstallation.Create(
                DefaultUserId, installationId: 42, org, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        Factory.GitHubApi.Reset();
        Factory.GitHubApi.OrgMembers.Add("haxnted");

        AuthenticateAs("platform-participant", studentId);
        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{githubStepId}/complete/", content: null);
        resp.EnsureSuccessStatusCode();

        await AssertStepCompletionAsync(studentId, planId, githubStepId, completed: true);
    }

    [Fact]
    public async Task Github_step_complete_succeeds_via_live_check_without_invitation()
    {
        // #1148: GitHub привязан, invitation-строки нет, кэш orgs пуст — live-проверка
        // берёт логин из AuthService и подтверждает членство.
        const string org = "sachkovtech";
        Guid planId = await CreatePlanWithGithubStepAsync(org);
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid githubStepId = await StepIdAsync(planId, PlanOnboardingStepType.GITHUB);
        await SeedActiveInstallationAsync(org);
        Factory.AuthClient.GithubLoginsByUserId[studentId] = "linked-student";
        Factory.GitHubApi.Reset();
        Factory.GitHubApi.OrgMembers.Add("linked-student");

        AuthenticateAs("platform-participant", studentId);
        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{githubStepId}/complete/", content: null);
        resp.EnsureSuccessStatusCode();

        await AssertStepCompletionAsync(studentId, planId, githubStepId, completed: true);
    }

    [Fact]
    public async Task Github_step_live_check_prefers_linked_login_over_stale_invitation()
    {
        // Юзер перепривязал GitHub: invitation хранит старый логин, в org — новый.
        const string org = "sachkovtech";
        Guid planId = await CreatePlanWithGithubStepAsync(org);
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid githubStepId = await StepIdAsync(planId, PlanOnboardingStepType.GITHUB);
        await SeedGithubInvitationAsync(
            GithubOrgInvitation.CreateFailed(planId, studentId, "old-login", org, "github_user_not_found", DateTimeOffset.UtcNow.AddDays(-30)));
        await SeedActiveInstallationAsync(org);
        Factory.AuthClient.GithubLoginsByUserId[studentId] = "new-login";
        Factory.GitHubApi.Reset();
        Factory.GitHubApi.OrgMembers.Add("new-login");

        AuthenticateAs("platform-participant", studentId);
        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{githubStepId}/complete/", content: null);
        resp.EnsureSuccessStatusCode();

        await AssertStepCompletionAsync(studentId, planId, githubStepId, completed: true);
    }

    [Fact]
    public async Task Github_step_complete_reports_verification_unavailable_when_github_fails()
    {
        const string org = "sachkovtech";
        Guid planId = await CreatePlanWithGithubStepAsync(org);
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid githubStepId = await StepIdAsync(planId, PlanOnboardingStepType.GITHUB);
        await SeedActiveInstallationAsync(org);
        Factory.AuthClient.GithubLoginsByUserId[studentId] = "linked-student";
        Factory.GitHubApi.Reset();
        Factory.GitHubApi.MembershipCheckFails = true;

        AuthenticateAs("platform-participant", studentId);
        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{githubStepId}/complete/", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("onboarding.github.verification.unavailable", await ErrorCodeAsync(resp));
        await AssertStepCompletionAsync(studentId, planId, githubStepId, completed: false);
    }

    [Fact]
    public async Task Github_step_complete_reports_verification_unavailable_without_installation()
    {
        const string org = "sachkovtech";
        Guid planId = await CreatePlanWithGithubStepAsync(org);
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid githubStepId = await StepIdAsync(planId, PlanOnboardingStepType.GITHUB);
        Factory.AuthClient.GithubLoginsByUserId[studentId] = "linked-student";

        AuthenticateAs("platform-participant", studentId);
        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{githubStepId}/complete/", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal("onboarding.github.verification.unavailable", await ErrorCodeAsync(resp));
        await AssertStepCompletionAsync(studentId, planId, githubStepId, completed: false);
    }

    [Fact]
    public async Task Github_step_complete_rejected_when_invitation_pending_and_not_member()
    {
        const string org = "sachkovtech";
        Guid planId = await CreatePlanWithGithubStepAsync(org);
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        Guid githubStepId = await StepIdAsync(planId, PlanOnboardingStepType.GITHUB);
        await SeedGithubInvitationAsync(
            GithubOrgInvitation.CreatePending(planId, studentId, "octocat", org, githubInvitationId: 777, DateTimeOffset.UtcNow));

        AuthenticateAs("platform-participant", studentId);
        // Invitation still PENDING and not yet a member → must not complete.

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{githubStepId}/complete/", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        await AssertStepCompletionAsync(studentId, planId, githubStepId, completed: false);
    }

    [Fact]
    public async Task Already_completed_telegram_step_advances_stuck_cursor()
    {
        // #596 reproducer: «Далее» нажата на курсоре, припаркованном на УЖЕ завершённом
        // TELEGRAM-шаге (back-nav через return-to). До фикса handler делал голый no-op success
        // без AdvanceTo → курсор не двигался, юзер залипал. Теперь «Далее» продвигает курсор.
        Guid planId = await CreatePlanWithTelegramStepAsync();
        Guid studentId = Guid.NewGuid();
        Guid telegramStepId = await StepIdAsync(planId, PlanOnboardingStepType.TELEGRAM);
        await SeedOnboardingStuckOnCompletedStepAsync(studentId, planId, telegramStepId);

        AuthenticateAs("platform-participant", studentId);
        // not_member: «Далее» на уже завершённом шаге НЕ должно пере-верифицировать членство —
        // только продвинуть залипший курсор (идемпотентность завершённого шага).
        Factory.TelegramClient.MembershipResult = new PlanMembershipDto(IsMember: false, Status: "not_member");

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/onboarding/{planId}/steps/{telegramStepId}/complete/", content: null);
        resp.EnsureSuccessStatusCode();

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == studentId && o.PlanId == planId);
            Assert.Contains(telegramStepId, ob.CompletedStepIds);                 // остался завершённым
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

    private async Task AssertStepCompletionAsync(Guid userId, Guid planId, Guid stepId, bool completed)
    {
        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding ob = await db.UserPlanOnboardings
                .SingleAsync(o => o.UserId == userId && o.PlanId == planId);
            if (completed)
                Assert.Contains(stepId, ob.CompletedStepIds);
            else
                Assert.DoesNotContain(stepId, ob.CompletedStepIds);
        });
    }

    private async Task<Guid> StepIdAsync(Guid planId, PlanOnboardingStepType type) =>
        await ExecuteInDbAsync(async db => await db.PlanOnboardingSteps
            .Where(s => s.PlanId == planId && s.Type == type)
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

    private async Task<Guid> CreatePlanWithGithubStepAsync(string orgSlug)
    {
        Guid planId = await CreatePlanAsync();
        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            var result = plan.UpdateGithubOrg(orgSlug);
            Assert.True(result.IsSuccess);
            await db.SaveChangesAsync();
        });
        await EnableOnboardingAsync(planId); // org present → EnsureAutoStep(GITHUB) on enable
        return planId;
    }

    private async Task SeedGithubInvitationAsync(GithubOrgInvitation invitation) =>
        await ExecuteInDbAsync(async db =>
        {
            db.GithubOrgInvitations.Add(invitation);
            await db.SaveChangesAsync();
        });

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

    private async Task SeedActiveInstallationAsync(string org) =>
        await ExecuteInDbAsync(async db =>
        {
            db.AuthorGithubInstallations.Add(AuthorGithubInstallation.Create(
                DefaultUserId, installationId: 42, org, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage resp)
    {
        Envelope<object>? env = await resp.Content.ReadFromJsonAsync<Envelope<object>>();
        return env?.Error?.Messages is [var first, ..] ? first.Code : null;
    }

    private async Task EnableOnboardingAsync(Guid planId)
    {
        HttpResponseMessage resp = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));
        resp.EnsureSuccessStatusCode();
    }
}
