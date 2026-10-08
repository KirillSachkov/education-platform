using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Contracts.InviteLinks.Requests;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.InviteLinks;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetInvitePreviewTests : AccessServiceTestsBase
{
    public GetInvitePreviewTests(IntegrationTestsWebFactory factory) : base(factory) { }

    private async Task<Guid> CreatePlanAsync(string slug = "full-access")
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: "Полный доступ",
            ShortDescription: "Доступ ко всему",
            LongDescription: null,
            CoverFileId: null,
            Features: ["Все курсы", "Будущие курсы"],
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

    private async Task<InviteLinkDto> CreateInviteAsync(Guid planId, DateTimeOffset? expiresAt = null)
    {
        CreateInviteLinkRequest request = new(
            MultiUse: true,
            MaxUses: null,
            ExpiresAt: expiresAt,
            Label: "preview test");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);
        response.EnsureSuccessStatusCode();
        Envelope<InviteLinkDto>? envelope = await response.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        return envelope.Result!;
    }

    [Fact]
    public async Task Anonymous_can_preview_active_invite()
    {
        Guid planId = await CreatePlanAsync();
        InviteLinkDto invite = await CreateInviteAsync(planId);

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/invites/{invite.Token}/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<InvitePreviewDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InvitePreviewDto>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);

        InvitePreviewDto preview = envelope.Result!;
        Assert.True(preview.IsAvailable);
        Assert.Null(preview.UnavailableReason);
        Assert.Equal(nameof(PlanTier.FULL_ALL), preview.PlanTier);
        Assert.Equal("Полный доступ", preview.PlanDisplayName);
        Assert.Equal("Доступ ко всему", preview.PlanShortDescription);
        Assert.Equal(2, preview.PlanFeatures.Count);
        Assert.True(preview.IncludesFutureContent);
        Assert.Empty(preview.PlanCourseIds);
    }

    [Fact]
    public async Task Revoked_invite_preview_returns_unavailable()
    {
        Guid planId = await CreatePlanAsync();
        InviteLinkDto invite = await CreateInviteAsync(planId);

        HttpResponseMessage revoke = await AppHttpClient.PostAsync(
            $"/access/invites/{invite.Id}/revoke", content: null);
        revoke.EnsureSuccessStatusCode();

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/invites/{invite.Token}/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<InvitePreviewDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InvitePreviewDto>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);

        InvitePreviewDto preview = envelope.Result!;
        Assert.False(preview.IsAvailable);
        Assert.Equal("invite.revoked", preview.UnavailableReason);
    }

    [Fact]
    public async Task Expired_invite_preview_returns_unavailable()
    {
        Guid planId = await CreatePlanAsync();
        InviteLinkDto invite = await CreateInviteAsync(
            planId,
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/invites/{invite.Token}/preview");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<InvitePreviewDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<InvitePreviewDto>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);

        InvitePreviewDto preview = envelope.Result!;
        Assert.False(preview.IsAvailable);
        Assert.Equal("invite.expired", preview.UnavailableReason);
    }

    [Fact]
    public async Task Invalid_token_format_returns_404()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/access/invites/garbage/preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages,
            m => string.Equals(m.Code, "invite.not.found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Nonexistent_token_returns_404()
    {
        RemoveAuthentication();

        // Valid format (22 chars from base62 alphabet) but never stored.
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/access/invites/AAAAAAAAAAAAAAAAAAAAAA/preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages,
            m => string.Equals(m.Code, "invite.not.found", StringComparison.Ordinal));
    }
}
