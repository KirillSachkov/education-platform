using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Contracts.Plans.Requests;
using AccessService.Core.Features.Admin;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

/// <summary>
/// Tests for the ADMIN manual plan-revoke path (#414):
/// <c>GET /access/admin/users/{userId}/grants</c> (read) +
/// <c>POST /access/admin/grants/{grantId}/revoke</c> (revoke ANY grant).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class AdminRevokeGrantTests : AccessServiceTestsBase
{
    public AdminRevokeGrantTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Admin_revokes_another_users_grant_publishes_revoked_event()
    {
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();
        Guid grantId = await IssueGrantAsync(planId, recipient);

        AuthenticateAsAdmin();
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/admin/grants/{grantId}/revoke",
            JsonContent.Create(new AdminRevokeGrantRequest("policy violation")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grantId);
            Assert.Equal(PlanGrantStatus.REVOKED, grant.Status);
            Assert.Equal("policy violation", grant.RevokeReason);
            Assert.NotNull(grant.RevokedAt);
        });

        PlanGrantRevoked published = Assert.Single(
            OutboxCollector.OfType<PlanGrantRevoked>(), e => e.GrantId == grantId);
        Assert.Equal(recipient, published.UserId);
        Assert.Equal(planId, published.PlanId);
        Assert.Equal(planId, published.CanonicalTelegramPlanId);
    }

    [Fact]
    public async Task Non_admin_cannot_revoke_any_grant()
    {
        Guid planId = await CreatePlanAsync();
        Guid grantId = await IssueGrantAsync(planId, recipient: Guid.NewGuid());

        // platform-author owns the plan but is NOT admin — the admin route is ADMIN-only.
        AuthenticateAs("platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/admin/grants/{grantId}/revoke",
            JsonContent.Create(new AdminRevokeGrantRequest("nope")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grantId);
            Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
        });
    }

    [Fact]
    public async Task Idempotent_re_revoke_returns_ok_no_duplicate_event()
    {
        Guid planId = await CreatePlanAsync();
        Guid grantId = await IssueGrantAsync(planId, recipient: Guid.NewGuid());

        AuthenticateAsAdmin();

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/access/admin/grants/{grantId}/revoke",
            JsonContent.Create(new AdminRevokeGrantRequest("first")));
        first.EnsureSuccessStatusCode();

        OutboxCollector.Clear();

        HttpResponseMessage second = await AppHttpClient.PostAsync(
            $"/access/admin/grants/{grantId}/revoke",
            JsonContent.Create(new AdminRevokeGrantRequest("second")));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        // No second PlanGrantRevoked — idempotent no-op short-circuits before publish.
        Assert.Empty(OutboxCollector.OfType<PlanGrantRevoked>());

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grantId);
            Assert.Equal(PlanGrantStatus.REVOKED, grant.Status);
            // First reason preserved — re-revoke didn't overwrite.
            Assert.Equal("first", grant.RevokeReason);
        });
    }

    [Fact]
    public async Task Revoke_already_expired_grant_returns_ok_no_event()
    {
        // Regression (#430): an EXPIRED grant (e.g. transitioned by ExpiredGrantsSweeper)
        // must be an idempotent no-op for admin revoke — return 200 with the grant id,
        // NOT fall through to PlanGrant.Revoke() → grant.not.active → 409.
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();
        Guid grantId = await SeedGrantWithStatusAsync(planId, recipient, PlanGrantStatus.EXPIRED);

        AuthenticateAsAdmin();
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/admin/grants/{grantId}/revoke",
            JsonContent.Create(new AdminRevokeGrantRequest("policy violation")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Guid returnedId = (await response.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;
        Assert.Equal(grantId, returnedId);

        // No PlanGrantRevoked — idempotent no-op short-circuits before publish.
        Assert.Empty(OutboxCollector.OfType<PlanGrantRevoked>());

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grantId);
            // Stays EXPIRED — admin revoke didn't flip it to REVOKED.
            Assert.Equal(PlanGrantStatus.EXPIRED, grant.Status);
            Assert.Null(grant.RevokedAt);
        });
    }

    [Fact]
    public async Task Revoke_missing_grant_returns_404()
    {
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/admin/grants/{Guid.NewGuid()}/revoke",
            JsonContent.Create(new AdminRevokeGrantRequest("x")));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_empty_reason_returns_400()
    {
        Guid planId = await CreatePlanAsync();
        Guid grantId = await IssueGrantAsync(planId, recipient: Guid.NewGuid());

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/admin/grants/{grantId}/revoke",
            JsonContent.Create(new AdminRevokeGrantRequest("")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "grant.revoke.reason.required", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Admin_lists_user_grants_with_pinned_shape()
    {
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();
        Guid grantId = await IssueGrantAsync(planId, recipient);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/admin/users/{recipient}/grants");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<AdminUserGrantsResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<AdminUserGrantsResponse>>();
        Assert.NotNull(envelope?.Result);

        AdminUserGrantRow row = Assert.Single(envelope!.Result!.Items);
        // Pinned contract fields (#414).
        Assert.Equal(grantId, row.GrantId);
        Assert.Equal(planId, row.PlanId);
        Assert.Equal("Полный доступ", row.PlanDisplayName);
        Assert.Equal(nameof(PlanGrantStatus.ACTIVE), row.Status);
        Assert.Equal(nameof(PlanGrantSource.ADMIN_GRANT), row.Source);
        Assert.NotEqual(default, row.GrantedAt);
        Assert.Null(row.ExpiresAt);
        // Legacy aliases still populated for the existing admin cross-service UI.
        Assert.Equal(grantId, row.Id);
        Assert.Equal(row.GrantedAt, row.CreatedAt);
    }

    [Fact]
    public async Task Moderator_can_read_but_cannot_revoke()
    {
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();
        Guid grantId = await IssueGrantAsync(planId, recipient);

        AuthenticateAs("platform-moderator");

        HttpResponseMessage read = await AppHttpClient.GetAsync(
            $"/access/admin/users/{recipient}/grants");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        HttpResponseMessage revoke = await AppHttpClient.PostAsync(
            $"/access/admin/grants/{grantId}/revoke",
            JsonContent.Create(new AdminRevokeGrantRequest("mod try")));
        Assert.Equal(HttpStatusCode.Forbidden, revoke.StatusCode);
    }

    private async Task<Guid> CreatePlanAsync()
    {
        AuthenticateAs("platform-author");
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: $"admin-revoke-{Guid.NewGuid():N}",
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
        AuthenticateAs("platform-author");
        AdminGrantRequest request = new(recipient, planId, null);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>())!.Result!.Id;
    }

    /// <summary>
    /// Seeds a <see cref="PlanGrant"/> directly in the given non-ACTIVE status (mirrors
    /// the domain transition the sweeper / revoke would do), bypassing the HTTP endpoint.
    /// </summary>
    private async Task<Guid> SeedGrantWithStatusAsync(Guid planId, Guid recipient, PlanGrantStatus status)
    {
        PlanGrant grant = PlanGrant.Create(recipient, planId, PlanGrantSource.ADMIN_GRANT, sourceRef: null);
        if (status == PlanGrantStatus.REVOKED)
        {
            grant.Revoke(Guid.NewGuid(), "seeded");
        }
        else if (status == PlanGrantStatus.EXPIRED)
        {
            grant.Expire();
        }

        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        return grant.Id;
    }
}
