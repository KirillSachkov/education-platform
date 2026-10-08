using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.InviteLinks.Dtos;
using AccessService.Contracts.InviteLinks.Requests;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.PlanGrants;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class OutboxTests : AccessServiceTestsBase
{
    public OutboxTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Redeem_publishes_PlanGrantCreated_to_outbox()
    {
        // L2-тест: ловит «забыл flush'нуть outbox» — если handler не вызовет SaveChangesAsync,
        // TestOutboxCollector останется пустым.
        Guid planId = await CreatePlanAsync();
        InviteLinkDto invite = await CreateInviteAsync(planId);

        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentId);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/invites/{invite.Token}/redeem", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlanGrantCreated published = OutboxCollector.OfType<PlanGrantCreated>().Single();
        Assert.Equal(studentId, published.UserId);
        Assert.Equal(planId, published.PlanId);
        Assert.Equal(nameof(PlanTier.FULL_ALL), published.PlanTier);
        Assert.True(published.IncludesFutureContent);
        Assert.Equal(nameof(PlanGrantSource.INVITE_LINK), published.Source);
        Assert.Equal(invite.Id, published.SourceRef);
        Assert.Null(published.CourseId);
    }

    [Fact]
    public async Task AdminGrant_publishes_PlanGrantCreated_to_outbox()
    {
        Guid planId = await CreatePlanAsync(slug: "outbox-admin-test");

        Guid recipient = Guid.NewGuid();
        AdminGrantRequest request = new(recipient, planId, ExpiresAt: null);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlanGrantCreated published = OutboxCollector.OfType<PlanGrantCreated>().Single();
        Assert.Equal(recipient, published.UserId);
        Assert.Equal(planId, published.PlanId);
        Assert.Equal(nameof(PlanGrantSource.ADMIN_GRANT), published.Source);
        Assert.Equal(CurrentUserId, published.SourceRef);
    }

    [Fact]
    public async Task RevokeGrant_publishes_PlanGrantRevoked_to_outbox()
    {
        Guid planId = await CreatePlanAsync(slug: "outbox-revoke-test");

        Guid recipient = Guid.NewGuid();
        AdminGrantRequest grantRequest = new(recipient, planId, ExpiresAt: null);
        HttpResponseMessage grantResp = await AppHttpClient.PostAsJsonAsync(
            "/access/grants/admin/", grantRequest);
        grantResp.EnsureSuccessStatusCode();
        Guid grantId = (await grantResp.Content.ReadFromJsonAsync<Envelope<PlanGrantDto>>())!.Result!.Id;

        // Очищаем коллектор между шагами setup и act — нас интересует только revoke-публикация.
        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/access/grants/{grantId}/revoke",
            JsonContent.Create(new RevokeGrantRequest("test")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        PlanGrantRevoked published = OutboxCollector.OfType<PlanGrantRevoked>().Single();
        Assert.Equal(grantId, published.GrantId);
        Assert.Equal(recipient, published.UserId);
        Assert.Equal(planId, published.PlanId);
        Assert.Equal("test", published.Reason);
    }

    private async Task<Guid> CreatePlanAsync(string slug = "outbox-test")
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
        return (await response.Content.ReadFromJsonAsync<Envelope<InviteLinkDto>>())!.Result!;
    }
}
