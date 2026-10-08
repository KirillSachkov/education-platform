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

/// <summary>
/// Coverage для нового <c>DELETE /access/invites/{inviteId}/</c> endpoint'а
/// (issue #236, NEW-1). Закрывает hard-delete отдельно от revoke: invite
/// пропадает из списка, но активированные grant'ы продолжают существовать.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class DeleteInviteLinkTests : AccessServiceTestsBase
{
    public DeleteInviteLinkTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Author_deletes_own_invite_returns_200_and_removes_row()
    {
        Guid planId = await CreatePublishedPlanAsync();
        InviteLinkDto invite = await CreateInviteAsync(planId);

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/access/invites/{invite.Id}/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope.IsError);
        Assert.Equal(invite.Id, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            InviteLink? row = await db.InviteLinks.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == invite.Id);
            Assert.Null(row);
        });
    }

    [Fact]
    public async Task Foreign_author_cannot_delete_returns_403_and_keeps_row()
    {
        Guid planId = await CreatePublishedPlanAsync();
        InviteLinkDto invite = await CreateInviteAsync(planId);

        Guid foreignUserId = Guid.NewGuid();
        AuthenticateAs("platform-author", foreignUserId);

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/access/invites/{invite.Id}/");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m =>
            string.Equals(m.Code, "access.denied", StringComparison.Ordinal));

        // Row не удалён.
        await ExecuteInDbAsync(async db =>
        {
            bool stillExists = await db.InviteLinks.AsNoTracking()
                .AnyAsync(i => i.Id == invite.Id);
            Assert.True(stillExists);
        });
    }

    [Fact]
    public async Task Deleting_missing_invite_returns_404()
    {
        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/access/invites/{Guid.NewGuid()}/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope.IsError);
        Assert.NotNull(envelope.Error);
        Assert.Contains(envelope.Error!.Messages, m =>
            string.Equals(m.Code, "invite.not.found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Already_redeemed_grants_survive_invite_deletion()
    {
        // Контракт hard-delete: invite пропадает из списка автора, но grant'ы,
        // которые юзеры уже активировали по этому invite, остаются и продолжают
        // давать доступ. Revoke grant'а — отдельный flow.
        Guid authorId = Guid.NewGuid();
        AuthenticateAs("platform-author", authorId);

        Guid planId = await CreatePublishedPlanAsync(slug: "delete-with-grants");
        InviteLinkDto invite = await CreateInviteAsync(planId);

        // Симулируем активный grant, выпущенный через invite.
        Guid grantedUserId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(
                grantedUserId,
                planId,
                PlanGrantSource.INVITE_LINK,
                sourceRef: invite.Id);
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-author", authorId);
        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/access/invites/{invite.Id}/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            // invite ушёл
            bool inviteGone = !await db.InviteLinks.AsNoTracking()
                .AnyAsync(i => i.Id == invite.Id);
            Assert.True(inviteGone);

            // grant остался ACTIVE
            PlanGrant? grant = await db.PlanGrants.AsNoTracking()
                .FirstOrDefaultAsync(g => g.UserId == grantedUserId && g.PlanId == planId);
            Assert.NotNull(grant);
            Assert.Equal(PlanGrantStatus.ACTIVE, grant!.Status);
        });
    }

    [Fact]
    public async Task Admin_can_delete_any_authors_invite()
    {
        Guid authorId = Guid.NewGuid();
        AuthenticateAs("platform-author", authorId);
        Guid planId = await CreatePublishedPlanAsync(slug: "admin-delete-foreign");
        InviteLinkDto invite = await CreateInviteAsync(planId);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/access/invites/{invite.Id}/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            bool gone = !await db.InviteLinks.AsNoTracking()
                .AnyAsync(i => i.Id == invite.Id);
            Assert.True(gone);
        });
    }

    private async Task<Guid> CreatePublishedPlanAsync(string slug = "delete-test-plan")
    {
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: "Тестовый план",
            ShortDescription: "for delete tests",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 100_000,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync(
            "/access/plans/", request);
        createResponse.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await createResponse.Content.ReadFromJsonAsync<Envelope<Guid>>();
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
        Envelope<InviteLinkDto>? envelope = await response.Content
            .ReadFromJsonAsync<Envelope<InviteLinkDto>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        return envelope.Result!;
    }
}
