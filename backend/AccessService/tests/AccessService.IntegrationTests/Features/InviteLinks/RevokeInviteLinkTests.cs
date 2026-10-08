using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Contracts.InviteLinks.Requests;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.InviteLinks;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class RevokeInviteLinkTests : AccessServiceTestsBase
{
    public RevokeInviteLinkTests(IntegrationTestsWebFactory factory) : base(factory) { }

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
        Assert.NotNull(envelope);
        return envelope.Result;
    }

    private async Task<InviteLinkDto> CreateInviteAsync(Guid planId)
    {
        CreateInviteLinkRequest request = new(
            MultiUse: true,
            MaxUses: null,
            ExpiresAt: null,
            Label: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);
        response.EnsureSuccessStatusCode();
        Envelope<InviteLinkDto>? envelope = await response.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        return envelope.Result!;
    }

    [Fact]
    public async Task Author_revokes_own_plans_invite()
    {
        Guid planId = await CreatePlanAsync();
        InviteLinkDto invite = await CreateInviteAsync(planId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/invites/{invite.Id}/revoke", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(invite.Id, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            InviteLink? row = await db.InviteLinks.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == invite.Id);
            Assert.NotNull(row);
            Assert.False(row!.IsActive);
            Assert.NotNull(row.RevokedAt);
        });
    }

    [Fact]
    public async Task Foreign_author_cannot_revoke()
    {
        Guid planId = await CreatePlanAsync();
        InviteLinkDto invite = await CreateInviteAsync(planId);

        Guid foreignUserId = Guid.NewGuid();
        AuthenticateAs("platform-author", foreignUserId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/invites/{invite.Id}/revoke", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "access.denied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Revoking_missing_invite_returns_404()
    {
        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/invites/{Guid.NewGuid()}/revoke", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "invite.not.found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Revoke_is_idempotent()
    {
        Guid planId = await CreatePlanAsync();
        InviteLinkDto invite = await CreateInviteAsync(planId);

        HttpResponseMessage first = await AppHttpClient.PostAsync(
            $"/access/invites/{invite.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        HttpResponseMessage second = await AppHttpClient.PostAsync(
            $"/access/invites/{invite.Id}/revoke", content: null);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Envelope<Guid>? envelope = await second.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(invite.Id, envelope.Result);
    }
}
