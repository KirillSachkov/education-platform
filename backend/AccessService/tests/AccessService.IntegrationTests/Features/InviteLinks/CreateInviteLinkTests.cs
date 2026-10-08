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
public sealed class CreateInviteLinkTests : AccessServiceTestsBase
{
    public CreateInviteLinkTests(IntegrationTestsWebFactory factory) : base(factory) { }

    private async Task<Guid> CreatePlanAsync(string tier = nameof(PlanTier.FULL_ALL), string slug = "full-access")
    {
        CreatePlanRequest request = new(
            Tier: tier,
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

    [Fact]
    public async Task Author_creates_multiuse_invite_link()
    {
        Guid planId = await CreatePlanAsync();

        CreateInviteLinkRequest request = new(
            MultiUse: true,
            MaxUses: null,
            ExpiresAt: null,
            Label: "Q1 промо");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<InviteLinkDto>? envelope = await response.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);

        InviteLinkDto dto = envelope.Result!;
        Assert.Equal(planId, dto.PlanId);
        Assert.True(dto.MultiUse);
        Assert.Null(dto.MaxUses);
        Assert.Null(dto.ExpiresAt);
        Assert.Equal("Q1 промо", dto.Label);
        Assert.Equal(InviteToken.LENGTH, dto.Token.Length);
        Assert.True(dto.IsActive);
        Assert.Equal(0, dto.UsageCount);
        Assert.Equal(CurrentUserId, dto.CreatedBy);
        Assert.Null(dto.RevokedAt);
    }

    [Fact]
    public async Task Cannot_create_invite_for_archived_plan()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage archive = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        archive.EnsureSuccessStatusCode();

        CreateInviteLinkRequest request = new(
            MultiUse: false,
            MaxUses: 1,
            ExpiresAt: null,
            Label: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "plan.archived", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Foreign_author_cannot_create_invite()
    {
        Guid planId = await CreatePlanAsync();

        Guid foreignUserId = Guid.NewGuid();
        AuthenticateAs("platform-author", foreignUserId);

        CreateInviteLinkRequest request = new(
            MultiUse: true,
            MaxUses: null,
            ExpiresAt: null,
            Label: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "access.denied", StringComparison.Ordinal));
    }
}
