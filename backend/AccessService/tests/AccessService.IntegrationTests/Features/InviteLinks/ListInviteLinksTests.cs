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
public sealed class ListInviteLinksTests : AccessServiceTestsBase
{
    public ListInviteLinksTests(IntegrationTestsWebFactory factory) : base(factory) { }

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

    private async Task<InviteLinkDto> CreateInviteAsync(Guid planId, string? label = null)
    {
        CreateInviteLinkRequest request = new(
            MultiUse: true,
            MaxUses: null,
            ExpiresAt: null,
            Label: label);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", request);
        response.EnsureSuccessStatusCode();
        Envelope<InviteLinkDto>? envelope = await response.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        return envelope.Result!;
    }

    [Fact]
    public async Task Author_lists_own_plans_invites()
    {
        Guid planId = await CreatePlanAsync();
        InviteLinkDto first = await CreateInviteAsync(planId, "first");
        // Slight delay so CreatedAt strictly differs (Guid v7 also gives ordering, but be safe).
        await Task.Delay(10);
        InviteLinkDto second = await CreateInviteAsync(planId, "second");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/invites/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<InviteLinkDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<InviteLinkDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);

        List<InviteLinkDto> dtos = envelope.Result!;
        Assert.Equal(2, dtos.Count);
        // newest first
        Assert.Equal(second.Id, dtos[0].Id);
        Assert.Equal(first.Id, dtos[1].Id);
        Assert.True(dtos[0].CreatedAt >= dtos[1].CreatedAt);
    }

    [Fact]
    public async Task Foreign_author_cannot_list()
    {
        Guid planId = await CreatePlanAsync();
        await CreateInviteAsync(planId);

        Guid foreignUserId = Guid.NewGuid();
        AuthenticateAs("platform-author", foreignUserId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/invites/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m => string.Equals(m.Code, "access.denied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Empty_list_when_plan_has_no_invites()
    {
        Guid planId = await CreatePlanAsync();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/invites/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<List<InviteLinkDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<List<InviteLinkDto>>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result!);
    }
}
