using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class UpdatePlanTests : AccessServiceTestsBase
{
    public UpdatePlanTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Author_updates_own_plan()
    {
        CreatePlanRequest createRequest = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "full-access",
            DisplayName: "Полный доступ",
            ShortDescription: "Доступ ко всему",
            LongDescription: "**Маркдаун.**",
            CoverFileId: null,
            Features: new[] { "Все курсы" },
            PriceCents: 990_000,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 1);

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? createEnvelope = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(createEnvelope);
        Guid planId = createEnvelope.Result;

        UpdatePlanRequest updateRequest = new(
            DisplayName: "Полный доступ Pro",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 1_490_000,
            Currency: null,
            CourseIds: [],
            DisplayOrder: null);

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(planId, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.Equal("Полный доступ Pro", plan.DisplayName.Value);
            Assert.Equal(1_490_000, plan.PriceCents);
            // Untouched fields preserved
            Assert.Equal("Доступ ко всему", plan.ShortDescription);
            Assert.Equal("RUB", plan.Currency);
            Assert.Equal(1, plan.DisplayOrder);
        });
    }

    [Fact]
    public async Task Update_course_plan_changes_offer_type()
    {
        CreatePlanRequest createRequest = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: "course-offer-type",
            DisplayName: "Курс",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [Guid.NewGuid()],
            DisplayOrder: 0,
            OfferType: nameof(PlanOfferType.COURSE));

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? createEnvelope = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(createEnvelope);
        Guid planId = createEnvelope.Result;

        UpdatePlanRequest updateRequest = new(
            DisplayName: null,
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: null,
            CourseIds: [],
            DisplayOrder: null,
            OfferType: nameof(PlanOfferType.INTENSIVE));

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.Equal(PlanOfferType.INTENSIVE, plan.OfferType);
        });
    }

    [Fact]
    public async Task Update_full_all_plan_keeps_full_access_offer_type_when_course_supplied()
    {
        Guid planId = await CreateLifetimeAllPlanAsync("full-access-offer-type");

        UpdatePlanRequest updateRequest = new(
            DisplayName: null,
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: null,
            CourseIds: [],
            DisplayOrder: null,
            OfferType: nameof(PlanOfferType.COURSE));

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            // FULL_ALL tier forces FULL_ACCESS — supplied COURSE is ignored (no-op), not rejected.
            Assert.Equal(PlanOfferType.FULL_ACCESS, plan.OfferType);
        });
    }

    [Fact]
    public async Task Foreign_author_cannot_update_someone_elses_plan()
    {
        CreatePlanRequest createRequest = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: "dotnet",
            DisplayName: ".NET",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [Guid.NewGuid()],
            DisplayOrder: 0);

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? createEnvelope = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(createEnvelope);
        Guid planId = createEnvelope.Result;

        // Switch to a different author
        Guid foreignUserId = Guid.NewGuid();
        AuthenticateAs("platform-author", foreignUserId);

        UpdatePlanRequest updateRequest = new(
            DisplayName: "Hijacked",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: null,
            CourseIds: [],
            DisplayOrder: null);

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", updateRequest);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "access.denied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_returns_404_for_missing_plan()
    {
        UpdatePlanRequest updateRequest = new(
            DisplayName: "Anything",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: null,
            CourseIds: [],
            DisplayOrder: null);

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{Guid.NewGuid()}", updateRequest);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "plan.not.found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_sets_github_org_slug_normalized_to_lowercase()
    {
        Guid planId = await CreateLifetimeAllPlanAsync("full-access-gh");

        UpdatePlanRequest updateRequest = new(
            DisplayName: null,
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: null,
            CourseIds: [],
            DisplayOrder: null,
            GithubOrgSlug: "Miracle-Generation");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", updateRequest);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.Equal("miracle-generation", plan.GitHubOrg);
        });
    }

    [Fact]
    public async Task Update_with_empty_github_org_slug_clears_binding()
    {
        Guid planId = await CreateLifetimeAllPlanAsync("clear-gh");

        // First — set
        UpdatePlanRequest setRequest = new(
            DisplayName: null, ShortDescription: null, LongDescription: null, CoverFileId: null,
            Features: null, PriceCents: null, Currency: null, CourseIds: [], DisplayOrder: null,
            GithubOrgSlug: "team-alpha");
        await AppHttpClient.PatchAsJsonAsync($"/access/plans/{planId}", setRequest);

        // Second — clear
        UpdatePlanRequest clearRequest = new(
            DisplayName: null, ShortDescription: null, LongDescription: null, CoverFileId: null,
            Features: null, PriceCents: null, Currency: null, CourseIds: [], DisplayOrder: null,
            GithubOrgSlug: "");
        HttpResponseMessage clearResponse = await AppHttpClient.PatchAsJsonAsync($"/access/plans/{planId}", clearRequest);

        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Plan plan = await db.Plans.SingleAsync(p => p.Id == planId);
            Assert.Null(plan.GitHubOrg);
        });
    }

    [Fact]
    public async Task Update_rejects_invalid_github_org_slug()
    {
        Guid planId = await CreateLifetimeAllPlanAsync("reject-gh");

        UpdatePlanRequest updateRequest = new(
            DisplayName: null, ShortDescription: null, LongDescription: null, CoverFileId: null,
            Features: null, PriceCents: null, Currency: null, CourseIds: [], DisplayOrder: null,
            GithubOrgSlug: "-invalid-leading-dash");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}", updateRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.Contains(envelope.Error!.Messages,
            m => string.Equals(m.Code, "plan.github.org.invalid", StringComparison.Ordinal));
    }

    private async Task<Guid> CreateLifetimeAllPlanAsync(string slug)
    {
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

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/access/plans/", createRequest);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        return envelope.Result;
    }
}
