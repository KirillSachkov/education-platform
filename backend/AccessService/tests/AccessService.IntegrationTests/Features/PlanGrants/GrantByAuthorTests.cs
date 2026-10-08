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
public sealed class GrantByAuthorTests : AccessServiceTestsBase
{
    public GrantByAuthorTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Service_role_grants_lifetime_for_author_with_default_plan()
    {
        Guid authorId = DefaultUserId;
        await CreateDefaultLifetimePlanAsync();

        AuthenticateAs("platform-service", Guid.NewGuid());

        Guid recipient = Guid.NewGuid();
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/grants/lifetime-by-author",
            new GrantByAuthorRequest(recipient, authorId, "GITHUB_ORG", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<PlanGrantDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>();
        PlanGrantDto dto = envelope!.Result!;
        Assert.Equal(recipient, dto.UserId);
        Assert.Equal("GITHUB_ORG", dto.Source);
        Assert.Equal("ACTIVE", dto.Status);
    }

    [Fact]
    public async Task Returns_404_when_author_has_no_default_plan()
    {
        AuthenticateAs("platform-service", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/grants/lifetime-by-author",
            new GrantByAuthorRequest(Guid.NewGuid(), Guid.NewGuid(), "GITHUB_ORG", null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Idempotent_returns_existing_active_grant()
    {
        Guid authorId = DefaultUserId;
        await CreateDefaultLifetimePlanAsync();

        AuthenticateAs("platform-service", Guid.NewGuid());

        Guid recipient = Guid.NewGuid();
        var request = new GrantByAuthorRequest(recipient, authorId, "GITHUB_ORG", null);

        HttpResponseMessage first = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/grants/lifetime-by-author", request);
        first.EnsureSuccessStatusCode();
        Envelope<PlanGrantDto>? a = await first.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>();

        HttpResponseMessage second = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/grants/lifetime-by-author", request);
        second.EnsureSuccessStatusCode();
        Envelope<PlanGrantDto>? b = await second.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>();

        Assert.Equal(a!.Result!.Id, b!.Result!.Id);
    }

    [Fact]
    public async Task Rejects_disallowed_source()
    {
        Guid authorId = DefaultUserId;
        await CreateDefaultLifetimePlanAsync();

        AuthenticateAs("platform-service", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/grants/lifetime-by-author",
            new GrantByAuthorRequest(Guid.NewGuid(), authorId, "INVITE_LINK", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "grant.source.invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rejects_call_from_participant_role()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/access/grants/lifetime-by-author",
            new GrantByAuthorRequest(Guid.NewGuid(), Guid.NewGuid(), "GITHUB_ORG", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task CreateDefaultLifetimePlanAsync()
    {
        // Author auth state is the AccessServiceTestsBase default — DefaultUserId is the author.
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "default-lifetime-all",
            DisplayName: "Полный доступ",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: null,
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        create.EnsureSuccessStatusCode();
    }
}
