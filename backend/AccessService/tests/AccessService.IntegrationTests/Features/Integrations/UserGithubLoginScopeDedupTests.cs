using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Access.Events;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using Wolverine;
using Wolverine.Tracking;

namespace AccessService.IntegrationTests.Features.Integrations;

/// <summary>
///     #687 AC1 — scope-aware dedup в <c>UserGithubLoginAccessHandler</c>. Регрессия «месяц → навсегда»:
///     org-bound бессрочный FULL_ALL grant НЕ выдаётся поверх уже существующего активного grant'а
///     того же / более широкого scope (включая срочный «доступ на месяц» на ДРУГОМ плане).
///     L1 via <c>Host.InvokeMessageAndWaitAsync(new UserGithubLogin(...))</c> — см.
///     <c>UserGithubLoginAccessHandlerTests</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class UserGithubLoginScopeDedupTests : AccessServiceTestsBase
{
    private const string Org = "acme";

    public UserGithubLoginScopeDedupTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Does_not_mint_org_grant_when_user_holds_time_limited_full_grant_on_another_plan()
    {
        // Регрессия «месяц → навсегда»: у юзера есть ACTIVE срочный (ExpiresAt) FULL_ALL grant на
        // ТРИАЛ-плане A. Org "acme" привязана к ДРУГОМУ бессрочному FULL_ALL плану B. На
        // UserGithubLogin бессрочный org-grant на B выдавать НЕЛЬЗЯ — иначе месячный доступ
        // молча станет вечным.
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();

        Plan trialA = await SeedFullAllPlanAsync(authorId, trialDurationDays: 30);
        Plan orgB = await SeedFullAllPlanAsync(authorId, githubOrg: Org);
        await SeedGrantAsync(userId, trialA.Id, PlanGrantSource.PURCHASE,
            expiresAt: DateTimeOffset.UtcNow.AddDays(30));

        OutboxCollector.Clear();
        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(
            new UserGithubLogin(userId, "gh-user", new[] { Org }));

        await ExecuteInDbAsync(async db =>
        {
            Assert.False(
                await db.PlanGrants.AnyAsync(g => g.UserId == userId && g.PlanId == orgB.Id),
                "No GITHUB_ORG grant must be minted on plan B — the time-limited FULL_ALL already covers it");

            int total = await db.PlanGrants.CountAsync(g => g.UserId == userId);
            Assert.Equal(1, total); // only the original time-limited grant on A
        });

        Assert.Empty(OutboxCollector.OfType<PlanGrantCreated>());
    }

    [Fact]
    public async Task Mints_lifetime_org_grant_when_user_has_no_grants()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Plan orgB = await SeedFullAllPlanAsync(authorId, githubOrg: Org);

        OutboxCollector.Clear();
        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(
            new UserGithubLogin(userId, "gh-user", new[] { Org }));

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.SingleAsync(g => g.UserId == userId);
            Assert.Equal(orgB.Id, grant.PlanId);
            Assert.Equal(PlanGrantSource.GITHUB_ORG, grant.Source);
            Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
            Assert.Null(grant.ExpiresAt); // org-bound plan is lifetime → perpetual grant
        });

        Assert.Single(OutboxCollector.OfType<PlanGrantCreated>());
    }

    [Fact]
    public async Task Does_not_duplicate_when_user_already_holds_lifetime_grant_on_the_org_plan()
    {
        Guid authorId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        Plan orgB = await SeedFullAllPlanAsync(authorId, githubOrg: Org);
        await SeedGrantAsync(userId, orgB.Id, PlanGrantSource.GITHUB_ORG, expiresAt: null);

        OutboxCollector.Clear();
        IHost host = Services.GetRequiredService<IHost>();
        await host.InvokeMessageAndWaitAsync(
            new UserGithubLogin(userId, "gh-user", new[] { Org }));

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.PlanGrants.CountAsync(g => g.UserId == userId && g.PlanId == orgB.Id);
            Assert.Equal(1, count);
        });

        Assert.Empty(OutboxCollector.OfType<PlanGrantCreated>());
    }

    private async Task<Plan> SeedFullAllPlanAsync(
        Guid authorId, string? githubOrg = null, int? trialDurationDays = null)
    {
        Plan plan = Plan.Create(
            authorId,
            PlanTier.FULL_ALL,
            PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Plan").Value,
            courseIds: [],
            requestedCapabilities: null,
            offerType: null,
            trialDurationDays: trialDurationDays).Value;

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
