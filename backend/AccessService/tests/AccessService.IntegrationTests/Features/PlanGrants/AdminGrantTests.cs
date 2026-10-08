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
public sealed class AdminGrantTests : AccessServiceTestsBase
{
    public AdminGrantTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Admin_grants_user_directly()
    {
        Guid planId = await CreatePlanAsync();

        AuthenticateAsAdmin();

        Guid recipient = Guid.NewGuid();
        AdminGrantRequest request = new(recipient, planId, ExpiresAt: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PlanGrantDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>();
        Assert.NotNull(envelope?.Result);

        PlanGrantDto dto = envelope!.Result!;
        Assert.Equal(recipient, dto.UserId);
        Assert.Equal(planId, dto.PlanId);
        Assert.Equal(nameof(PlanGrantSource.ADMIN_GRANT), dto.Source);
        Assert.Equal(nameof(PlanGrantStatus.ACTIVE), dto.Status);
    }

    [Fact]
    public async Task Author_grants_on_own_plan()
    {
        // Default test identity is platform-author owning the plan.
        Guid planId = await CreatePlanAsync();

        Guid recipient = Guid.NewGuid();
        AdminGrantRequest request = new(recipient, planId, null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Author_cannot_grant_on_foreign_plan()
    {
        Guid planId = await CreatePlanAsync();

        // Switch to another author.
        AuthenticateAs("platform-author", Guid.NewGuid());

        AdminGrantRequest request = new(Guid.NewGuid(), planId, null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Idempotent_for_existing_active_grant()
    {
        Guid planId = await CreatePlanAsync();

        Guid recipient = Guid.NewGuid();
        AdminGrantRequest request = new(recipient, planId, null);

        HttpResponseMessage first = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);
        first.EnsureSuccessStatusCode();
        Guid firstId = (await first.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>())!.Result!.Id;

        HttpResponseMessage second = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);
        second.EnsureSuccessStatusCode();
        Guid secondId = (await second.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>())!.Result!.Id;

        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public async Task Without_grant_permission_returns_403()
    {
        Guid planId = await CreatePlanAsync();

        AuthenticateAs("platform-participant", Guid.NewGuid());

        AdminGrantRequest request = new(Guid.NewGuid(), planId, null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Plan_not_found_returns_404()
    {
        AdminGrantRequest request = new(Guid.NewGuid(), Guid.NewGuid(), null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archived_plan_returns_plan_archived()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage archive = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        archive.EnsureSuccessStatusCode();

        AdminGrantRequest request = new(Guid.NewGuid(), planId, null);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "plan.archived", StringComparison.Ordinal));
    }

    private async Task<Guid> CreatePlanAsync()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "admin-grant-test",
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
}
