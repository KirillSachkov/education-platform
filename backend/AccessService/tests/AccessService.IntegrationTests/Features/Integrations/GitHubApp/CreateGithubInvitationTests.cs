using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Integrations.GitHub;
using AccessService.Contracts.Plans.Requests;
using AccessService.Core.Features.Integrations.GitHubApp.Services;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using AccessService.IntegrationTests.Infrastructure;
using AuthService.Contracts;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Integrations.GitHubApp;

/// <summary>
///     Recovery-семантика `POST /access/integrations/github/invitations/` (#501).
///     Прод-кейс @haxnted: строка FAILED(no_installation) от 2026-05-13 возвращалась
///     идемпотентно навсегда — ни повтор после установки App, ни «я уже member»
///     не работали (17 застрявших строк на проде).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class CreateGithubInvitationTests : AccessServiceTestsBase
{
    private const string ORG = "sachkovtech";
    private const string LOGIN = "haxnted";

    public CreateGithubInvitationTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Create_short_circuits_to_accepted_when_user_already_org_member()
    {
        Factory.GitHubApi.Reset();
        Guid planId = await CreatePlanWithOrgAsync();
        await SeedActiveInstallationAsync();
        Factory.GitHubApi.OrgMembers.Add(LOGIN);

        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);
        GithubInvitationStatusResponse resp = await CreateInvitationAsync(planId);

        // Юзер уже member → ACCEPTED сразу, без отправки приглашения.
        Assert.Equal("ACCEPTED", resp.Status);
        Assert.Equal(0, Factory.GitHubApi.CreateOrgInvitationCalls);
    }

    [Fact]
    public async Task Create_reattempts_terminal_failed_row_and_heals_to_accepted()
    {
        // Точный прод-сценарий: FAILED(no_installation) строка из эпохи «App не установлен»;
        // App теперь установлен, юзер уже member → строка лечится в ACCEPTED.
        Factory.GitHubApi.Reset();
        Guid planId = await CreatePlanWithOrgAsync();
        await SeedActiveInstallationAsync();
        Factory.GitHubApi.OrgMembers.Add(LOGIN);

        Guid studentId = Guid.NewGuid();
        Guid staleRowId = await SeedInvitationAsync(
            GithubOrgInvitation.CreateFailed(planId, studentId, LOGIN, ORG, "no_installation", DateTimeOffset.UtcNow.AddDays(-30)));

        AuthenticateAs("platform-participant", studentId);
        GithubInvitationStatusResponse resp = await CreateInvitationAsync(planId);

        Assert.Equal("ACCEPTED", resp.Status);
        Assert.Equal(staleRowId, resp.Id); // та же строка, не дубль
        Assert.Null(resp.FailureReason);
    }

    [Fact]
    public async Task Create_resends_invitation_after_expired()
    {
        Factory.GitHubApi.Reset();
        Guid planId = await CreatePlanWithOrgAsync();
        await SeedActiveInstallationAsync();
        Factory.GitHubApi.CreateResult = new CreateInvitationResult.Created(888);

        Guid studentId = Guid.NewGuid();
        GithubOrgInvitation expired = GithubOrgInvitation.CreatePending(
            planId, studentId, LOGIN, ORG, githubInvitationId: 111, DateTimeOffset.UtcNow.AddDays(-10));
        expired.MarkExpired(DateTimeOffset.UtcNow.AddDays(-1));
        Guid staleRowId = await SeedInvitationAsync(expired);

        AuthenticateAs("platform-participant", studentId);
        GithubInvitationStatusResponse resp = await CreateInvitationAsync(planId);

        Assert.Equal("PENDING", resp.Status);
        Assert.Equal(staleRowId, resp.Id);
        Assert.Equal(1, Factory.GitHubApi.CreateOrgInvitationCalls);
    }

    [Fact]
    public async Task Create_returns_pending_row_idempotently_without_api_calls()
    {
        Factory.GitHubApi.Reset();
        Guid planId = await CreatePlanWithOrgAsync();
        await SeedActiveInstallationAsync();
        Factory.GitHubApi.OrgMembers.Add(LOGIN); // даже member — PENDING не трогаем

        Guid studentId = Guid.NewGuid();
        await SeedInvitationAsync(GithubOrgInvitation.CreatePending(
            planId, studentId, LOGIN, ORG, githubInvitationId: 555, DateTimeOffset.UtcNow));

        AuthenticateAs("platform-participant", studentId);
        GithubInvitationStatusResponse resp = await CreateInvitationAsync(planId);

        Assert.Equal("PENDING", resp.Status);
        Assert.Equal(0, Factory.GitHubApi.CreateOrgInvitationCalls);
        Assert.Equal(0, Factory.GitHubApi.MembershipChecks);
    }

    [Fact]
    public async Task Create_keeps_failed_no_installation_when_app_still_missing()
    {
        // Re-attempt без установленного App: строка остаётся FAILED(no_installation),
        // эндпоинт отвечает 200 (UI показывает «свяжитесь с автором» + кнопку повтора).
        Factory.GitHubApi.Reset();
        Guid planId = await CreatePlanWithOrgAsync(); // installation НЕ сидим

        Guid studentId = Guid.NewGuid();
        Guid staleRowId = await SeedInvitationAsync(
            GithubOrgInvitation.CreateFailed(planId, studentId, LOGIN, ORG, "no_installation", DateTimeOffset.UtcNow.AddDays(-5)));

        AuthenticateAs("platform-participant", studentId);
        GithubInvitationStatusResponse resp = await CreateInvitationAsync(planId);

        Assert.Equal("FAILED", resp.Status);
        Assert.Equal("no_installation", resp.FailureReason);
        Assert.Equal(staleRowId, resp.Id);
    }

    [Fact]
    public async Task Create_rejects_user_without_current_active_grant_on_plan()
    {
        Factory.GitHubApi.Reset();
        Guid planId = await CreatePlanWithOrgAsync();
        await SeedActiveInstallationAsync();

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/integrations/github/invitations/",
            new CreateGithubInvitationRequest(planId, LOGIN));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, Factory.GitHubApi.MembershipChecks);
        Assert.Equal(0, Factory.GitHubApi.CreateOrgInvitationCalls);
    }

    [Fact]
    public async Task Create_rejects_github_login_not_linked_to_current_platform_user()
    {
        Factory.GitHubApi.Reset();
        Guid planId = await CreatePlanWithOrgAsync();
        await SeedActiveInstallationAsync();
        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);
        await SeedActiveGrantAsync(studentId, planId);
        Factory.AuthClient.GithubLoginsByUserId[studentId] = LOGIN;

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/integrations/github/invitations/",
            new CreateGithubInvitationRequest(planId, "someone-else"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, Factory.GitHubApi.MembershipChecks);
        Assert.Equal(0, Factory.GitHubApi.CreateOrgInvitationCalls);
    }

    [Fact]
    public async Task Create_rejects_archived_plan_even_with_active_grant()
    {
        Factory.GitHubApi.Reset();
        Guid planId = await CreatePlanWithOrgAsync();
        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);
        await SeedActiveGrantAsync(studentId, planId);
        Factory.AuthClient.GithubLoginsByUserId[studentId] = LOGIN;
        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.True(plan.Archive().IsSuccess);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/integrations/github/invitations/",
            new CreateGithubInvitationRequest(planId, LOGIN));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, Factory.GitHubApi.CreateOrgInvitationCalls);
    }

    private async Task<GithubInvitationStatusResponse> CreateInvitationAsync(Guid planId)
    {
        await SeedActiveGrantAsync(CurrentUserId, planId);
        Factory.AuthClient.GithubLoginsByUserId[CurrentUserId] = LOGIN;

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            "/access/integrations/github/invitations/",
            new CreateGithubInvitationRequest(planId, LOGIN));
        resp.EnsureSuccessStatusCode();
        Envelope<GithubInvitationStatusResponse>? env = await resp.Content
            .ReadFromJsonAsync<Envelope<GithubInvitationStatusResponse>>();
        Assert.NotNull(env);
        Assert.False(env.IsError);
        return env.Result!;
    }

    private async Task SeedActiveGrantAsync(Guid userId, Guid planId) =>
        await ExecuteInDbAsync(async db =>
        {
            if (!await db.PlanGrants.AnyAsync(g =>
                    g.UserId == userId
                    && g.PlanId == planId
                    && g.Status == PlanGrantStatus.ACTIVE))
            {
                db.PlanGrants.Add(PlanGrant.Create(
                    userId,
                    planId,
                    PlanGrantSource.ADMIN_GRANT,
                    sourceRef: null));
                await db.SaveChangesAsync();
            }
        });

    private async Task SeedActiveInstallationAsync() =>
        await ExecuteInDbAsync(async db =>
        {
            db.AuthorGithubInstallations.Add(AuthorGithubInstallation.Create(
                DefaultUserId, installationId: 42, ORG, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

    private async Task<Guid> SeedInvitationAsync(GithubOrgInvitation invitation)
    {
        await ExecuteInDbAsync(async db =>
        {
            db.GithubOrgInvitations.Add(invitation);
            await db.SaveChangesAsync();
        });
        return invitation.Id;
    }

    private async Task<Guid> CreatePlanWithOrgAsync()
    {
        AuthenticateAs("platform-author", DefaultUserId);
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
        Guid planId = env!.Result;

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            var result = plan.UpdateGithubOrg(ORG);
            Assert.True(result.IsSuccess);
            await db.SaveChangesAsync();
        });
        return planId;
    }
}
