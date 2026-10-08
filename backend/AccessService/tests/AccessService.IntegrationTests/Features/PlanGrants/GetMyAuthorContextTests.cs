using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Contracts.InviteLinks.Requests;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMyAuthorContextTests : AccessServiceTestsBase
{
    public GetMyAuthorContextTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        RemoveAuthentication();
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/me/author-context/{Guid.NewGuid()}/");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task User_without_grants_returns_registered_tier()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/me/author-context/{Guid.NewGuid()}/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        AuthorContextDto? ctx = (await resp.Content
            .ReadFromJsonAsync<Envelope<AuthorContextDto>>())!.Result;

        Assert.NotNull(ctx);
        Assert.False(ctx!.HasAnyGrant);
        Assert.Equal("registered", ctx.HighestTier);
        Assert.Empty(ctx.Grants);
    }

    [Fact]
    public async Task User_with_lifetime_grant_returns_lifetime_tier()
    {
        // Author A (test default user) создаёт plan + invite.
        Guid authorA = DefaultUserId;
        (Guid planId, string token) = await CreateLifetimePlanAndInviteAsync();

        // Student B redeems invite.
        Guid studentB = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentB);
        HttpResponseMessage redeem = await AppHttpClient.PostAsync(
            $"/access/invites/{token}/redeem", content: null);
        redeem.EnsureSuccessStatusCode();

        // B requests his author-context для author A.
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/me/author-context/{authorA}/");
        resp.EnsureSuccessStatusCode();

        AuthorContextDto? ctx = (await resp.Content
            .ReadFromJsonAsync<Envelope<AuthorContextDto>>())!.Result;

        Assert.NotNull(ctx);
        Assert.True(ctx!.HasAnyGrant);
        Assert.Equal("full_all", ctx.HighestTier);
        Assert.Single(ctx.Grants);
        Assert.Equal(planId, ctx.Grants[0].PlanId);
    }

    [Fact]
    public async Task User_with_full_grant_for_other_author_returns_full_all_tier()
    {
        // Author A creates plan, student B redeems → has global FULL_ALL access.
        (Guid planId, string token) = await CreateLifetimePlanAndInviteAsync();
        Guid studentB = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentB);
        await AppHttpClient.PostAsync($"/access/invites/{token}/redeem", content: null);

        // B requests author-context для какого-то другого автора → global grant applies.
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/me/author-context/{Guid.NewGuid()}/");
        resp.EnsureSuccessStatusCode();

        AuthorContextDto? ctx = (await resp.Content
            .ReadFromJsonAsync<Envelope<AuthorContextDto>>())!.Result;

        Assert.True(ctx!.HasAnyGrant);
        Assert.Equal("full_all", ctx.HighestTier);
        Assert.Single(ctx.Grants);
        Assert.Equal(planId, ctx.Grants[0].PlanId);
    }

    private async Task<(Guid planId, string token)> CreateLifetimePlanAndInviteAsync()
    {
        // Default auth — platform-author с DefaultUserId — создаёт plan.
        AuthenticateAs("platform-author");

        CreatePlanRequest planRequest = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: $"author-context-test-{Guid.NewGuid():N}",
            DisplayName: "Полный доступ",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage planResp = await AppHttpClient.PostAsJsonAsync("/access/plans/", planRequest);
        planResp.EnsureSuccessStatusCode();
        Guid planId = (await planResp.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;

        CreateInviteLinkRequest inviteRequest = new(true, null, null, null);
        HttpResponseMessage inviteResp = await AppHttpClient.PostAsJsonAsync(
            $"/access/plans/{planId}/invites/", inviteRequest);
        inviteResp.EnsureSuccessStatusCode();
        InviteLinkDto invite = (await inviteResp.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>())!.Result!;

        return (planId, invite.Token);
    }
}
