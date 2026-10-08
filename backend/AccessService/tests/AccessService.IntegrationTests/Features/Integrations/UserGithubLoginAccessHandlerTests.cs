using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Messaging.IntegrationEvents.Access.Events;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using SharedKernel;
using Wolverine;
using Wolverine.Tracking;

namespace AccessService.IntegrationTests.Features.Integrations;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class UserGithubLoginAccessHandlerTests : AccessServiceTestsBase
{
    public UserGithubLoginAccessHandlerTests(IntegrationTestsWebFactory factory) : base(factory) { }

    // L1 паттерн (см. docs/agents/wolverine-tests.md): InvokeMessageAndWaitAsync обходит
    // routing и invokes handler in-process. Проверяем эффект на БД (grant создан) + на
    // OutboxCollector (PlanGrantCreated опубликован через тестовый IOutboxService).
    [Fact]
    public async Task Issues_grant_when_org_matches_active_plan()
    {
        Guid planId = await CreatePlanWithGithubOrgAsync("dotnet-team", slug: "gh-1");

        Guid studentId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        // Mixed-case org должен нормализоваться к lowercase в handler'е.
        await host.InvokeMessageAndWaitAsync(
            new UserGithubLogin(studentId, "gh-user", new[] { "DotNet-Team", "other-org" }));

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.SingleAsync(g => g.UserId == studentId);
            Assert.Equal(planId, grant.PlanId);
            Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
            Assert.Equal(PlanGrantSource.GITHUB_ORG, grant.Source);
        });
    }

    [Fact]
    public async Task Idempotent_when_user_already_has_active_grant()
    {
        Guid planId = await CreatePlanWithGithubOrgAsync("repeat-org", slug: "gh-2");
        Guid studentId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        // First call — issues grant и публикует PlanGrantCreated.
        await host.InvokeMessageAndWaitAsync(
            new UserGithubLogin(studentId, "gh-user", new[] { "repeat-org" }));

        int firstCount = OutboxCollector.OfType<PlanGrantCreated>().Count();
        Assert.Equal(1, firstCount);

        // Second call — must NOT publish a second PlanGrantCreated.
        await host.InvokeMessageAndWaitAsync(
            new UserGithubLogin(studentId, "gh-user", new[] { "repeat-org" }));

        Assert.Equal(firstCount, OutboxCollector.OfType<PlanGrantCreated>().Count());

        // DB still has exactly one grant.
        await ExecuteInDbAsync(async db =>
        {
            int count = await db.PlanGrants.CountAsync(g => g.UserId == studentId && g.PlanId == planId);
            Assert.Equal(1, count);
        });
    }

    [Fact]
    public async Task No_op_when_no_plans_match_orgs()
    {
        await CreatePlanWithGithubOrgAsync("plan-org-x", slug: "gh-3");

        Guid studentId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        OutboxCollector.Clear();
        await host.InvokeMessageAndWaitAsync(
            new UserGithubLogin(studentId, "gh-user", new[] { "completely-unrelated-org" }));

        Assert.Empty(OutboxCollector.OfType<PlanGrantCreated>());

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.PlanGrants.CountAsync(g => g.UserId == studentId);
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public async Task No_op_when_orgs_list_is_empty()
    {
        await CreatePlanWithGithubOrgAsync("any-org", slug: "gh-4");

        Guid studentId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        OutboxCollector.Clear();
        await host.InvokeMessageAndWaitAsync(
            new UserGithubLogin(studentId, "gh-user", Array.Empty<string>()));

        Assert.Empty(OutboxCollector.OfType<PlanGrantCreated>());
    }

    [Fact]
    public async Task Skips_archived_plans()
    {
        Guid planId = await CreatePlanWithGithubOrgAsync("archived-org", slug: "gh-5");

        HttpResponseMessage archiveResponse = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        archiveResponse.EnsureSuccessStatusCode();

        Guid studentId = Guid.NewGuid();
        IHost host = Services.GetRequiredService<IHost>();

        OutboxCollector.Clear();
        await host.InvokeMessageAndWaitAsync(
            new UserGithubLogin(studentId, "gh-user", new[] { "archived-org" }));

        Assert.Empty(OutboxCollector.OfType<PlanGrantCreated>());
    }

    private async Task<Guid> CreatePlanWithGithubOrgAsync(string orgSlug, string slug)
    {
        // Use a per-test author so partial-unique on github_org_slug doesn't clash across tests.
        AuthenticateAs("platform-author", Guid.NewGuid());

        CreatePlanRequest createRequest = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: "Plan",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync(
            "/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Guid planId = envelope.Result;

        UpdatePlanRequest updateRequest = new(
            DisplayName: null, ShortDescription: null, LongDescription: null, CoverFileId: null,
            Features: null, PriceCents: null, Currency: null, CourseIds: [], DisplayOrder: null,
            GithubOrgSlug: orgSlug);
        HttpResponseMessage updateResponse = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", updateRequest);
        updateResponse.EnsureSuccessStatusCode();

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.Equal(orgSlug.ToLowerInvariant(), plan.GitHubOrg);
            Assert.True(plan.IsActive, "Plan must be active for handler to match");
            Assert.Null(plan.ArchivedAt);
        });

        return planId;
    }
}
