using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Onboarding;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using AccessService.Domain.Onboarding;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using PlatformAuth.Authorization;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Admin;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class RecheckGithubMembershipForUserTests : AccessServiceTestsBase
{
    private const string ORG = "sachkovtech";
    private const string LOGIN = "octocat";

    public RecheckGithubMembershipForUserTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Member_completes_pending_github_step()
    {
        Guid planId = await CreatePlanWithGithubStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        await SeedActiveInstallationAsync();

        Factory.GitHubApi.Reset();
        Factory.GitHubApi.OrgMembers.Add(LOGIN);
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/github/recheck/",
            new { githubLogin = LOGIN });

        response.EnsureSuccessStatusCode();

        RecheckGithubMembershipResponse result = await ReadResultAsync(response);
        Assert.True(result.Completed);
        Assert.Equal("member", result.Status);

        Guid githubStepId = await StepIdAsync(planId);
        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding onboarding = await db.UserPlanOnboardings
                .SingleAsync(x => x.UserId == studentId && x.PlanId == planId);
            Assert.Contains(githubStepId, onboarding.CompletedStepIds);
        });
    }

    [Fact]
    public async Task Non_member_leaves_github_step_pending()
    {
        Guid planId = await CreatePlanWithGithubStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        await SeedActiveInstallationAsync();

        Factory.GitHubApi.Reset();
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/github/recheck/",
            new RecheckGithubMembershipRequest(LOGIN));
        response.EnsureSuccessStatusCode();

        RecheckGithubMembershipResponse result = await ReadResultAsync(response);
        Assert.False(result.Completed);
        Assert.Equal("not_member", result.Status);
        await AssertGithubStepCompletedAsync(studentId, planId, expected: false);
    }

    [Fact]
    public async Task Github_failure_reports_unknown_without_completing_step()
    {
        Guid planId = await CreatePlanWithGithubStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);
        await SeedActiveInstallationAsync();

        Factory.GitHubApi.Reset();
        Factory.GitHubApi.MembershipCheckFails = true;
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/github/recheck/",
            new RecheckGithubMembershipRequest(LOGIN));
        response.EnsureSuccessStatusCode();

        RecheckGithubMembershipResponse result = await ReadResultAsync(response);
        Assert.False(result.Completed);
        Assert.Equal("unknown", result.Status);
        await AssertGithubStepCompletedAsync(studentId, planId, expected: false);
    }

    [Fact]
    public async Task Missing_installation_reports_unknown_without_calling_github()
    {
        Guid planId = await CreatePlanWithGithubStepAsync();
        Guid studentId = Guid.NewGuid();
        await SeedOnboardingAtFirstStepAsync(studentId, planId);

        Factory.GitHubApi.Reset();
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/github/recheck/",
            new RecheckGithubMembershipRequest(LOGIN));
        response.EnsureSuccessStatusCode();

        RecheckGithubMembershipResponse result = await ReadResultAsync(response);
        Assert.False(result.Completed);
        Assert.Equal("unknown", result.Status);
        Assert.Equal(0, Factory.GitHubApi.MembershipChecks);
        await AssertGithubStepCompletedAsync(studentId, planId, expected: false);
    }

    [Fact]
    public async Task Invalid_login_is_rejected_before_github_call()
    {
        Factory.GitHubApi.Reset();
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{Guid.NewGuid()}/plans/{Guid.NewGuid()}/github/recheck/",
            new RecheckGithubMembershipRequest("../octocat"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, Factory.GitHubApi.MembershipChecks);
    }

    [Fact]
    public async Task Missing_login_is_rejected_before_github_call()
    {
        Factory.GitHubApi.Reset();
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{Guid.NewGuid()}/plans/{Guid.NewGuid()}/github/recheck/",
            new RecheckGithubMembershipRequest(null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, Factory.GitHubApi.MembershipChecks);
    }

    [Fact]
    public async Task Already_completed_step_advances_stuck_cursor_without_github_call()
    {
        Guid planId = await CreatePlanWithGithubStepAsync();
        Guid studentId = Guid.NewGuid();
        Guid githubStepId = await StepIdAsync(planId);
        await SeedOnboardingStuckOnCompletedStepAsync(studentId, planId, githubStepId);

        Factory.GitHubApi.Reset();
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{studentId}/plans/{planId}/github/recheck/",
            new RecheckGithubMembershipRequest(LOGIN));
        response.EnsureSuccessStatusCode();

        RecheckGithubMembershipResponse result = await ReadResultAsync(response);
        Assert.True(result.Completed);
        Assert.Equal("member", result.Status);
        Assert.Equal(0, Factory.GitHubApi.MembershipChecks);

        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding onboarding = await db.UserPlanOnboardings
                .SingleAsync(x => x.UserId == studentId && x.PlanId == planId);
            Assert.Contains(githubStepId, onboarding.CompletedStepIds);
            Assert.NotEqual(githubStepId, onboarding.CurrentStepId);
        });
    }

    [Fact]
    public async Task Participant_is_forbidden()
    {
        Guid studentId = Guid.NewGuid();
        AuthenticateAs(PlatformRoles.PARTICIPANT, studentId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{studentId}/plans/{Guid.NewGuid()}/github/recheck/",
            new RecheckGithubMembershipRequest(LOGIN));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Moderator_is_forbidden()
    {
        AuthenticateAs(PlatformRoles.MODERATOR, Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{Guid.NewGuid()}/plans/{Guid.NewGuid()}/github/recheck/",
            new RecheckGithubMembershipRequest(LOGIN));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreatePlanWithGithubStepAsync()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: $"plan-{Guid.NewGuid():N}",
            DisplayName: "Test Plan",
            ShortDescription: "desc",
            LongDescription: "long",
            CoverFileId: null,
            Features: ["f1"],
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        create.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await create.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Guid planId = envelope!.Result;

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(x => x.Id == planId);
            Assert.True(plan.UpdateGithubOrg(ORG).IsSuccess);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage enable = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/onboarding-flow/",
            new SetOnboardingEnabledRequest(IsEnabled: true));
        enable.EnsureSuccessStatusCode();
        return planId;
    }

    private async Task SeedOnboardingAtFirstStepAsync(Guid userId, Guid planId)
    {
        await ExecuteInDbAsync(async db =>
        {
            Guid firstStepId = await db.PlanOnboardingSteps
                .Where(x => x.PlanId == planId)
                .OrderBy(x => x.SortOrder)
                .Select(x => x.Id)
                .FirstAsync();
            UserPlanOnboarding onboarding = UserPlanOnboarding.Start(userId, planId, DateTimeOffset.UtcNow);
            onboarding.SetCurrentStep(firstStepId);
            db.UserPlanOnboardings.Add(onboarding);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedActiveInstallationAsync()
    {
        await ExecuteInDbAsync(async db =>
        {
            db.AuthorGithubInstallations.Add(AuthorGithubInstallation.Create(
                DefaultUserId, installationId: 42, ORG, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedOnboardingStuckOnCompletedStepAsync(
        Guid userId,
        Guid planId,
        Guid completedStepId)
    {
        await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding onboarding = UserPlanOnboarding.Start(
                userId, planId, DateTimeOffset.UtcNow);
            Assert.True(onboarding.CompleteStep(completedStepId).IsSuccess);
            onboarding.SetCurrentStep(completedStepId);
            db.UserPlanOnboardings.Add(onboarding);
            await db.SaveChangesAsync();
        });
    }

    private async Task<Guid> StepIdAsync(Guid planId) =>
        await ExecuteInDbAsync(async db => await db.PlanOnboardingSteps
            .Where(x => x.PlanId == planId && x.Type == PlanOnboardingStepType.GITHUB)
            .Select(x => x.Id)
            .SingleAsync());

    private async Task AssertGithubStepCompletedAsync(Guid userId, Guid planId, bool expected)
    {
        Guid stepId = await StepIdAsync(planId);
        bool completed = await ExecuteInDbAsync(async db =>
        {
            UserPlanOnboarding onboarding = await db.UserPlanOnboardings
                .SingleAsync(x => x.UserId == userId && x.PlanId == planId);
            return onboarding.CompletedStepIds.Contains(stepId);
        });
        Assert.Equal(expected, completed);
    }

    private static async Task<RecheckGithubMembershipResponse> ReadResultAsync(HttpResponseMessage response)
    {
        Envelope<RecheckGithubMembershipResponse>? envelope = await response.Content
            .ReadFromJsonAsync<Envelope<RecheckGithubMembershipResponse>>();
        Assert.NotNull(envelope);
        return envelope!.Result!;
    }
}
