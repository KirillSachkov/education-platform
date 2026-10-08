using AccessService.Domain;
using AccessService.Domain.Integrations.GitHub;
using AccessService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;
using Wolverine;
using Wolverine.Tracking;

namespace AccessService.IntegrationTests.Features.Integrations;

/// <summary>
///     #687 AC6 — <c>RemoveOrgMemberOnGrantEndedHandler</c>: на <see cref="PlanGrantExpired"/> /
///     <see cref="PlanGrantRevoked"/> юзер исключается из GitHub-org, если у него больше нет ACTIVE
///     grant'а, покрывающего scope привязанного к org плана. Ошибки GitHub API retryable, через
///     <see cref="FakeGitHubAppApiClient.RemoveOrgMemberAsync"/>. L1 via
///     <c>Host.InvokeMessageAndWaitAsync</c> (на той же очереди работает и Redis-sync handler — это ok).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class RemoveOrgMemberOnGrantEndedHandlerTests : AccessServiceTestsBase
{
    private const long InstallationId = 4242;
    private const string Org = "acme";
    private const string Login = "octocat";

    public RemoveOrgMemberOnGrantEndedHandlerTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Expired_with_no_remaining_coverage_removes_org_member()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Plan orgPlan = await SeedFullAllPlanAsync(authorId, githubOrg: Org);
        await SeedInstallationAsync(authorId, InstallationId, Org);
        await SeedAcceptedInvitationAsync(orgPlan.Id, userId, Login, Org);
        // User has NO remaining active grant.

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new PlanGrantExpired(
            Guid.NewGuid(), userId, orgPlan.Id, nameof(PlanTier.FULL_ALL), authorId, null,
            DateTimeOffset.UtcNow));

        RemoveOrgMemberCall call = Assert.Single(Factory.GitHubApi.RemoveOrgMemberCalls);
        Assert.Equal(InstallationId, call.InstallationId);
        Assert.Equal(Org, call.OrgLogin);
        Assert.Equal(Login, call.Username);
    }

    [Fact]
    public async Task Expired_with_remaining_full_grant_does_not_remove()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Plan orgPlan = await SeedFullAllPlanAsync(authorId, githubOrg: Org);
        await SeedInstallationAsync(authorId, InstallationId, Org);
        await SeedAcceptedInvitationAsync(orgPlan.Id, userId, Login, Org);

        // User STILL has an active FULL_ALL grant (on another plan) that covers the org plan's scope.
        Plan coveringC = await SeedFullAllPlanAsync(authorId);
        await SeedGrantAsync(userId, coveringC.Id, PlanGrantSource.PURCHASE, expiresAt: null);

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new PlanGrantExpired(
            Guid.NewGuid(), userId, orgPlan.Id, nameof(PlanTier.FULL_ALL), authorId, null,
            DateTimeOffset.UtcNow));

        Assert.Empty(Factory.GitHubApi.RemoveOrgMemberCalls);
    }

    [Fact]
    public async Task Revoked_with_no_remaining_coverage_removes_org_member()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Plan orgPlan = await SeedFullAllPlanAsync(authorId, githubOrg: Org);
        await SeedInstallationAsync(authorId, InstallationId, Org);
        await SeedAcceptedInvitationAsync(orgPlan.Id, userId, Login, Org);

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new PlanGrantRevoked(
            Guid.NewGuid(), userId, orgPlan.Id, "manual revoke", DateTimeOffset.UtcNow));

        RemoveOrgMemberCall call = Assert.Single(Factory.GitHubApi.RemoveOrgMemberCalls);
        Assert.Equal(InstallationId, call.InstallationId);
        Assert.Equal(Org, call.OrgLogin);
        Assert.Equal(Login, call.Username);
    }

    [Fact]
    public async Task Archived_plan_removes_member_despite_still_active_grant_row()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Plan orgPlan = await SeedFullAllPlanAsync(authorId, githubOrg: Org);
        await SeedInstallationAsync(authorId, InstallationId, Org);
        await SeedAcceptedInvitationAsync(orgPlan.Id, userId, Login, Org);
        await SeedGrantAsync(userId, orgPlan.Id, PlanGrantSource.PURCHASE, expiresAt: null);
        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == orgPlan.Id);
            Assert.True(plan.Archive().IsSuccess);
            await db.SaveChangesAsync();
        });

        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(new PlanEntitlementsChanged(
            orgPlan.Id, DateTimeOffset.UtcNow));

        RemoveOrgMemberCall call = Assert.Single(Factory.GitHubApi.RemoveOrgMemberCalls);
        Assert.Equal(Login, call.Username);
    }

    [Fact]
    public async Task Transient_github_removal_failure_is_retryable()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Plan orgPlan = await SeedFullAllPlanAsync(authorId, githubOrg: Org);
        await SeedInstallationAsync(authorId, InstallationId, Org);
        await SeedAcceptedInvitationAsync(orgPlan.Id, userId, Login, Org);
        Factory.GitHubApi.RemoveOrgMemberResult = Result.Failure<bool, Error>(
            Error.Failure("github.unavailable", "temporary"));
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        Core.Features.Integrations.RemoveOrgMemberOnGrantEndedHandler handler =
            ActivatorUtilities.CreateInstance<Core.Features.Integrations.RemoveOrgMemberOnGrantEndedHandler>(
                scope.ServiceProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new PlanGrantExpired(
                Guid.NewGuid(), userId, orgPlan.Id, nameof(PlanTier.FULL_ALL), authorId, null,
                DateTimeOffset.UtcNow),
            CancellationToken.None));
    }

    private async Task<Plan> SeedFullAllPlanAsync(Guid authorId, string? githubOrg = null)
    {
        Plan plan = Plan.Create(
            authorId,
            PlanTier.FULL_ALL,
            PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Plan").Value,
            courseIds: [],
            requestedCapabilities: null,
            offerType: null,
            trialDurationDays: null).Value;

        if (githubOrg is not null)
        {
            plan.UpdateGithubOrg(githubOrg);
        }

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan;
    }

    private async Task SeedInstallationAsync(Guid authorId, long installationId, string orgLogin)
    {
        AuthorGithubInstallation installation =
            AuthorGithubInstallation.Create(authorId, installationId, orgLogin, DateTimeOffset.UtcNow);
        await ExecuteInDbAsync(async db =>
        {
            db.AuthorGithubInstallations.Add(installation);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedAcceptedInvitationAsync(
        Guid planId, Guid userId, string githubLogin, string orgLogin)
    {
        GithubOrgInvitation invitation = GithubOrgInvitation.CreateAlreadyMember(
            planId, userId, githubLogin, orgLogin, DateTimeOffset.UtcNow);
        await ExecuteInDbAsync(async db =>
        {
            db.GithubOrgInvitations.Add(invitation);
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedGrantAsync(
        Guid userId, Guid planId, PlanGrantSource source, DateTimeOffset? expiresAt)
    {
        PlanGrant grant = PlanGrant.Create(userId, planId, source, sourceRef: null, expiresAt: expiresAt);
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
    }
}
