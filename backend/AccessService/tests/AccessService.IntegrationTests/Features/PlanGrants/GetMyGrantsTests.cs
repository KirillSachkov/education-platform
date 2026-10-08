using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Contracts.InviteLinks.Requests;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetMyGrantsTests : AccessServiceTestsBase
{
    public GetMyGrantsTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task User_with_grant_sees_it()
    {
        (Guid planId, string token) = await CreatePlanAndInviteAsync();

        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);

        HttpResponseMessage redeem = await AppHttpClient.PostAsync(
            $"/access/invites/{token}/redeem", content: null);
        redeem.EnsureSuccessStatusCode();

        HttpResponseMessage list = await AppHttpClient.GetAsync("/access/me/grants/");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        Envelope<List<PlanGrantDto>>? envelope =
            await list.Content.ReadFromJsonAsync<Envelope<List<PlanGrantDto>>>();
        Assert.NotNull(envelope?.Result);

        PlanGrantDto grant = envelope!.Result!.Single();
        Assert.Equal(studentId, grant.UserId);
        Assert.Equal(planId, grant.PlanId);
        Assert.Equal(nameof(PlanGrantStatus.ACTIVE), grant.Status);
    }

    [Fact]
    public async Task User_without_grants_gets_empty_list()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage list = await AppHttpClient.GetAsync("/access/me/grants/");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        Envelope<List<PlanGrantDto>>? envelope =
            await list.Content.ReadFromJsonAsync<Envelope<List<PlanGrantDto>>>();
        Assert.NotNull(envelope?.Result);
        Assert.Empty(envelope!.Result!);
    }

    [Fact]
    public async Task Trial_grant_plan_summary_exposes_trial_duration()
    {
        (_, string token) = await CreatePlanAndInviteAsync(isTrial: true);

        AuthenticateAs("platform-participant", Guid.NewGuid());
        (await AppHttpClient.PostAsync($"/access/invites/{token}/redeem", content: null))
            .EnsureSuccessStatusCode();

        HttpResponseMessage list = await AppHttpClient.GetAsync("/access/me/grants/");
        list.EnsureSuccessStatusCode();

        using JsonDocument json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        JsonElement plan = json.RootElement.GetProperty("result")[0].GetProperty("plan");
        Assert.Equal(nameof(PlanTier.FULL_ALL), plan.GetProperty("tier").GetString());
        Assert.False(plan.TryGetProperty("kind", out _));
        Assert.Equal(30, plan.GetProperty("trialDurationDays").GetInt32());
    }

    [Fact]
    public async Task Anonymous_is_unauthorized()
    {
        RemoveAuthentication();

        HttpResponseMessage list = await AppHttpClient.GetAsync("/access/me/grants/");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
    }

    [Fact]
    public async Task Other_users_grants_are_not_returned()
    {
        (_, string token) = await CreatePlanAndInviteAsync();

        // Student A redeems.
        Guid studentA = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentA);
        HttpResponseMessage redeem = await AppHttpClient.PostAsync(
            $"/access/invites/{token}/redeem", content: null);
        redeem.EnsureSuccessStatusCode();

        // Student B asks for own grants — should be empty.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage list = await AppHttpClient.GetAsync("/access/me/grants/");
        list.EnsureSuccessStatusCode();

        Envelope<List<PlanGrantDto>>? envelope =
            await list.Content.ReadFromJsonAsync<Envelope<List<PlanGrantDto>>>();
        Assert.Empty(envelope!.Result!);
    }

    private async Task<(Guid planId, string token)> CreatePlanAndInviteAsync(bool isTrial = false)
    {
        CreatePlanRequest planRequest = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: "my-grants-test",
            DisplayName: "Полный доступ",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: null,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0,
            IsTrial: isTrial);

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
