using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Contracts.InviteLinks.Requests;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class RedeemTests : AccessServiceTestsBase
{
    public RedeemTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Authenticated_user_redeems_invite_and_gets_grant()
    {
        (Guid planId, string token) = await CreatePlanAndInviteAsync();

        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/invites/{token}/redeem", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<PlanGrantDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);

        PlanGrantDto dto = envelope.Result!;
        Assert.Equal(studentId, dto.UserId);
        Assert.Equal(planId, dto.PlanId);
        Assert.Equal(nameof(PlanGrantSource.INVITE_LINK), dto.Source);
        Assert.Equal(nameof(PlanGrantStatus.ACTIVE), dto.Status);
        Assert.Null(dto.ExpiresAt);
        Assert.Null(dto.RevokedAt);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync();
            Assert.Equal(studentId, grant.UserId);
            Assert.Equal(planId, grant.PlanId);

            InviteRedemption redemption = await db.InviteRedemptions.AsNoTracking().SingleAsync();
            Assert.Equal(grant.Id, redemption.PlanGrantId);

            InviteLink invite = await db.InviteLinks.AsNoTracking().SingleAsync();
            Assert.Equal(1, invite.UsageCount);
        });
    }

    [Fact]
    public async Task Redeem_is_idempotent_for_same_user()
    {
        (_, string token) = await CreatePlanAndInviteAsync();

        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/access/invites/{token}/redeem", content: null);
        first.EnsureSuccessStatusCode();
        Envelope<PlanGrantDto>? firstEnvelope =
            await first.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>();

        HttpResponseMessage second = await AppHttpClient.PostAsync(
            $"/access/invites/{token}/redeem", content: null);
        second.EnsureSuccessStatusCode();
        Envelope<PlanGrantDto>? secondEnvelope =
            await second.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>();

        Assert.NotNull(firstEnvelope?.Result);
        Assert.NotNull(secondEnvelope?.Result);
        Assert.Equal(firstEnvelope!.Result.Id, secondEnvelope!.Result.Id);

        await ExecuteInDbAsync(async db =>
        {
            int grantsCount = await db.PlanGrants.CountAsync();
            Assert.Equal(1, grantsCount);

            InviteLink invite = await db.InviteLinks.AsNoTracking().SingleAsync();
            Assert.Equal(1, invite.UsageCount);
        });
    }

    [Fact]
    public async Task Revoked_invite_returns_400_invite_revoked()
    {
        (_, string token) = await CreatePlanAndInviteAsync();

        // Revoke as author (default test identity).
        HttpResponseMessage list = await AppHttpClient.GetAsync(
            $"/access/plans/{await GetPlanIdFromTokenAsync(token)}/invites/");
        list.EnsureSuccessStatusCode();
        Envelope<List<InviteLinkDto>>? listEnv =
            await list.Content.ReadFromJsonAsync<Envelope<List<InviteLinkDto>>>();
        Guid inviteId = listEnv!.Result!.Single().Id;

        HttpResponseMessage revoke = await AppHttpClient.PostAsync(
            $"/access/invites/{inviteId}/revoke", content: null);
        revoke.EnsureSuccessStatusCode();

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/invites/{token}/redeem", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages,
            m => string.Equals(m.Code, "invite.revoked", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Expired_invite_returns_400_invite_expired()
    {
        Guid planId = await CreatePlanAsync();

        CreateInviteLinkRequest request = new(
            MultiUse: true,
            MaxUses: null,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            Label: null);

        HttpResponseMessage createInvite = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);
        createInvite.EnsureSuccessStatusCode();
        InviteLinkDto invite =
            (await createInvite.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>())!.Result!;

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/invites/{invite.Token}/redeem", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope?.Error);
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "invite.expired", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Single_use_invite_blocks_second_user()
    {
        Guid planId = await CreatePlanAsync();

        CreateInviteLinkRequest request = new(
            MultiUse: false,
            MaxUses: null,
            ExpiresAt: null,
            Label: null);

        HttpResponseMessage createInvite = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);
        createInvite.EnsureSuccessStatusCode();
        InviteLinkDto invite =
            (await createInvite.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>())!.Result!;

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/access/invites/{invite.Token}/redeem", content: null);
        first.EnsureSuccessStatusCode();

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage second = await AppHttpClient.PostAsync(
            $"/access/invites/{invite.Token}/redeem", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        Envelope? envelope = await second.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "invite.usage.exhausted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Anonymous_redeem_is_unauthorized()
    {
        (_, string token) = await CreatePlanAndInviteAsync();

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/invites/{token}/redeem", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_token_format_returns_404()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            "/access/invites/garbage/redeem", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "invite.not.found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Archived_plan_returns_plan_archived()
    {
        (Guid planId, string token) = await CreatePlanAndInviteAsync();

        HttpResponseMessage archive = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        archive.EnsureSuccessStatusCode();

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/invites/{token}/redeem", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "plan.archived", StringComparison.Ordinal));
    }

    private async Task<Guid> CreatePlanAsync(string slug = "full-access")
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: "Полный доступ",
            ShortDescription: "Доступ ко всему",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 990_000,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        response.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        return envelope!.Result;
    }

    private async Task<(Guid planId, string token)> CreatePlanAndInviteAsync()
    {
        Guid planId = await CreatePlanAsync();

        CreateInviteLinkRequest request = new(
            MultiUse: true,
            MaxUses: null,
            ExpiresAt: null,
            Label: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);
        response.EnsureSuccessStatusCode();
        InviteLinkDto invite =
            (await response.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>())!.Result!;
        return (planId, invite.Token);
    }

    private async Task<Guid> GetPlanIdFromTokenAsync(string token)
    {
        return await ExecuteInDbAsync(async db =>
            await db.InviteLinks.AsNoTracking()
                .Where(i => i.Token.Value == token)
                .Select(i => i.PlanId)
                .SingleAsync());
    }
}
