using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Contracts.InviteLinks.Requests;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Contracts.Plans.Dtos;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetPlanStatsTests : AccessServiceTestsBase
{
    public GetPlanStatsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Empty_plan_returns_zero_totals_and_dense_zero_timeseries()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/stats/?periodDays=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlanStatsDto stats = await ReadStatsAsync(response);

        Assert.Equal(0, stats.Totals.Total);
        Assert.Equal(0, stats.Totals.Active);
        Assert.Equal(0, stats.Totals.Revoked);
        Assert.Equal(0, stats.Totals.Expired);
        Assert.Equal(0, stats.PeriodCounters.Last7Days);
        Assert.Empty(stats.SourceBreakdown);
        Assert.Empty(stats.InviteLinks);
        // Dense timeseries — 7 days back through today = 8 inclusive points (0..7).
        Assert.True(stats.Timeseries.Count >= 7 && stats.Timeseries.Count <= 8);
        Assert.All(stats.Timeseries, p => Assert.Equal(0, p.Count));
    }

    [Fact]
    public async Task Totals_and_source_breakdown_reflect_existing_grants()
    {
        Guid planId = await CreatePlanAsync();
        await IssueAdminGrantAsync(planId, Guid.NewGuid());
        await IssueAdminGrantAsync(planId, Guid.NewGuid());
        await IssueAdminGrantAsync(planId, Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/stats/");
        response.EnsureSuccessStatusCode();

        PlanStatsDto stats = await ReadStatsAsync(response);

        Assert.Equal(3, stats.Totals.Total);
        Assert.Equal(3, stats.Totals.Active);
        Assert.Equal(0, stats.Totals.Revoked);
        Assert.Equal(3, stats.PeriodCounters.Last7Days);
        Assert.Equal(3, stats.PeriodCounters.Last30Days);

        PlanGrantSourceBreakdownDto adminRow = Assert.Single(stats.SourceBreakdown);
        Assert.Equal(nameof(PlanGrantSource.ADMIN_GRANT), adminRow.Source);
        Assert.Equal(3, adminRow.Count);

        // Один день, 3 grant'а — последний bucket = 3.
        Assert.Contains(stats.Timeseries, p =>
            p.Count == 3 && p.BySource.TryGetValue(nameof(PlanGrantSource.ADMIN_GRANT), out long c) && c == 3);
    }

    [Fact]
    public async Task Revoked_grant_counted_separately_from_active()
    {
        Guid planId = await CreatePlanAsync();
        Guid recipient = Guid.NewGuid();

        AdminGrantRequest grantRequest = new(recipient, planId, null);
        HttpResponseMessage grantResp = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", grantRequest);
        grantResp.EnsureSuccessStatusCode();
        Guid grantId = (await grantResp.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>())!.Result!.Id;

        HttpResponseMessage revoke = await AppHttpClient.PostAsync(
            $"/access/grants/{grantId}/revoke",
            JsonContent.Create(new RevokeGrantRequest("test")));
        revoke.EnsureSuccessStatusCode();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/stats/");
        response.EnsureSuccessStatusCode();

        PlanStatsDto stats = await ReadStatsAsync(response);

        Assert.Equal(1, stats.Totals.Total);
        Assert.Equal(0, stats.Totals.Active);
        Assert.Equal(1, stats.Totals.Revoked);
    }

    [Fact]
    public async Task Invite_link_stats_aggregate_redemptions()
    {
        (Guid planId, string token, Guid inviteId) = await CreatePlanAndInviteAsync("promo-jan");

        // Two distinct users redeem the same invite.
        Guid userA = Guid.NewGuid();
        AuthenticateAs("platform-participant", userA);
        (await AppHttpClient.PostAsync($"/access/invites/{token}/redeem", content: null))
            .EnsureSuccessStatusCode();

        Guid userB = Guid.NewGuid();
        AuthenticateAs("platform-participant", userB);
        (await AppHttpClient.PostAsync($"/access/invites/{token}/redeem", content: null))
            .EnsureSuccessStatusCode();

        // Re-authenticate as the plan owner (DefaultUserId) to read stats.
        AuthenticateAs("platform-author", DefaultUserId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/stats/");
        response.EnsureSuccessStatusCode();

        PlanStatsDto stats = await ReadStatsAsync(response);

        PlanInviteLinkStatsDto link = Assert.Single(stats.InviteLinks);
        Assert.Equal(inviteId, link.InviteLinkId);
        Assert.Equal("promo-jan", link.Label);
        Assert.Equal(2, link.ActivationsCount);
        Assert.Equal(2, link.UniqueGrantsCount);
        Assert.NotNull(link.FirstActivationAt);
        Assert.NotNull(link.LastActivationAt);
        Assert.True(link.IsActive);

        Assert.Equal(2, stats.Totals.Active);
        PlanGrantSourceBreakdownDto inviteRow = Assert.Single(stats.SourceBreakdown);
        Assert.Equal(nameof(PlanGrantSource.INVITE_LINK), inviteRow.Source);
        Assert.Equal(2, inviteRow.Count);
    }

    [Fact]
    public async Task Foreign_author_cannot_read_stats()
    {
        Guid planId = await CreatePlanAsync();

        AuthenticateAs("platform-author", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/stats/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Period_days_above_max_is_clamped()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/stats/?periodDays=99999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        PlanStatsDto stats = await ReadStatsAsync(response);
        // MAX_PERIOD_DAYS = 365 → timeseries не должен взорваться.
        Assert.InRange(stats.Timeseries.Count, 365, 367);
    }

    [Fact]
    public async Task Plan_not_found_returns_404()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{Guid.NewGuid()}/stats/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> CreatePlanAsync()
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "stats-test-" + Guid.NewGuid().ToString("N")[..6],
            DisplayName: "Stats test plan",
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

    private async Task IssueAdminGrantAsync(Guid planId, Guid recipient)
    {
        AdminGrantRequest request = new(recipient, planId, null);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<(Guid planId, string token, Guid inviteId)> CreatePlanAndInviteAsync(
        string? label)
    {
        Guid planId = await CreatePlanAsync();

        CreateInviteLinkRequest request = new(
            MultiUse: true,
            MaxUses: null,
            ExpiresAt: null,
            Label: label);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);
        response.EnsureSuccessStatusCode();
        InviteLinkDto invite =
            (await response.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>())!.Result!;
        return (planId, invite.Token, invite.Id);
    }

    private static async Task<PlanStatsDto> ReadStatsAsync(HttpResponseMessage response)
    {
        Envelope<PlanStatsDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PlanStatsDto>>();
        Assert.NotNull(envelope?.Result);
        return envelope!.Result!;
    }
}
