using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetUserGrantsInternalTests : AccessServiceTestsBase
{
    public GetUserGrantsInternalTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Admin_can_call_internal_endpoint()
    {
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();
        await IssueGrantAsync(planId, recipient);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/access/users/{recipient}/grants");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<PlanGrantDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<PlanGrantDto>>>();
        Assert.NotNull(envelope?.Result);
        Assert.Single(envelope!.Result!);
        Assert.Equal(recipient, envelope.Result![0].UserId);
        Assert.Equal(planId, envelope.Result[0].TelegramBindingPlanId);
        Assert.Contains(nameof(PlanCapabilities.COMMUNITY_ACCESS), envelope.Result[0].Capabilities!);
    }

    [Fact]
    public async Task Endpoint_enriches_trial_and_non_community_grants_for_telegram_authorization()
    {
        Guid authorId = Guid.CreateVersion7();
        Guid recipient = Guid.CreateVersion7();
        Plan lifetime = CreateDomainPlan(authorId, "telegram-lifetime", PlanTier.FULL_ALL);
        lifetime.Publish();
        Plan trial = CreateDomainPlan(authorId, "telegram-trial", PlanTier.FULL_ALL, trialDurationDays: 30);
        Plan materialsOnly = CreateDomainPlan(
            authorId,
            "telegram-materials-only",
            PlanTier.COURSE,
            [Guid.CreateVersion7()],
            [nameof(PlanCapabilities.VIEW_MATERIALS)]);

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.AddRange(lifetime, trial, materialsOnly);
            db.PlanGrants.AddRange(
                PlanGrant.Create(recipient, trial.Id, PlanGrantSource.TRIAL, null),
                PlanGrant.Create(recipient, materialsOnly.Id, PlanGrantSource.ADMIN_GRANT, null));
            await db.SaveChangesAsync();
        });

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/access/users/{recipient}/grants");
        response.EnsureSuccessStatusCode();

        Envelope<List<PlanGrantDto>> envelope =
            (await response.Content.ReadFromJsonAsync<Envelope<List<PlanGrantDto>>>())!;
        PlanGrantDto trialGrant = Assert.Single(envelope.Result!, grant => grant.PlanId == trial.Id);
        Assert.Equal(lifetime.Id, trialGrant.TelegramBindingPlanId);
        Assert.Contains(nameof(PlanCapabilities.COMMUNITY_ACCESS), trialGrant.Capabilities!);

        PlanGrantDto materialsGrant = Assert.Single(
            envelope.Result!, grant => grant.PlanId == materialsOnly.Id);
        Assert.Equal(materialsOnly.Id, materialsGrant.TelegramBindingPlanId);
        Assert.DoesNotContain(nameof(PlanCapabilities.COMMUNITY_ACCESS), materialsGrant.Capabilities!);
    }

    [Fact]
    public async Task Service_role_can_call_internal_endpoint()
    {
        Guid recipient = Guid.NewGuid();

        AuthenticateAs("platform-service", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/access/users/{recipient}/grants");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<PlanGrantDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<PlanGrantDto>>>();
        Assert.Empty(envelope!.Result!);
    }

    [Fact]
    public async Task Author_role_cannot_call_internal_endpoint()
    {
        // Default test identity is platform-author.
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/access/users/{Guid.NewGuid()}/grants");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/access/users/{Guid.NewGuid()}/grants");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> CreatePlanAsync()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "internal-grants-test",
            DisplayName: "Полный доступ",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;
    }

    private static Plan CreateDomainPlan(
        Guid authorId,
        string slug,
        PlanTier tier,
        IReadOnlyList<Guid>? courseIds = null,
        IReadOnlyList<string>? capabilities = null,
        int? trialDurationDays = null) =>
        Plan.Create(
            authorId,
            tier,
            PlanSlug.Of(slug).Value,
            PlanDisplayName.Of(slug).Value,
            courseIds ?? [],
            capabilities,
            trialDurationDays: trialDurationDays).Value;

    private async Task IssueGrantAsync(Guid planId, Guid recipient)
    {
        AdminGrantRequest request = new(recipient, planId, null);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);
        response.EnsureSuccessStatusCode();
    }
}
