using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class RevokeGrantTests : AccessServiceTestsBase
{
    public RevokeGrantTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Author_revokes_grant_on_own_plan()
    {
        Guid planId = await CreatePlanAsync();
        Guid grantId = await IssueGrantAsync(planId, recipient: Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/grants/{grantId}/revoke", JsonContent.Create(new RevokeGrantRequest("test")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grantId);
            Assert.Equal(PlanGrantStatus.REVOKED, grant.Status);
            Assert.Equal("test", grant.RevokeReason);
            Assert.NotNull(grant.RevokedAt);
        });
    }

    [Fact]
    public async Task Foreign_author_cannot_revoke()
    {
        Guid planId = await CreatePlanAsync();
        Guid grantId = await IssueGrantAsync(planId, recipient: Guid.NewGuid());

        AuthenticateAs("platform-author", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/grants/{grantId}/revoke", JsonContent.Create(new RevokeGrantRequest(null)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Double_revoke_returns_grant_not_active()
    {
        Guid planId = await CreatePlanAsync();
        Guid grantId = await IssueGrantAsync(planId, recipient: Guid.NewGuid());

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/access/grants/{grantId}/revoke", JsonContent.Create(new RevokeGrantRequest(null)));
        first.EnsureSuccessStatusCode();

        HttpResponseMessage second = await AppHttpClient.PostAsync(
            $"/access/grants/{grantId}/revoke", JsonContent.Create(new RevokeGrantRequest(null)));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Envelope? envelope = await second.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "grant.not.active", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Grant_not_found_returns_404()
    {
        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/grants/{Guid.NewGuid()}/revoke",
            JsonContent.Create(new RevokeGrantRequest(null)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> CreatePlanAsync()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "revoke-test",
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

    private async Task<Guid> IssueGrantAsync(Guid planId, Guid recipient)
    {
        AdminGrantRequest request = new(recipient, planId, null);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>())!.Result!.Id;
    }
}
