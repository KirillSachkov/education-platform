using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.HomePins;
using AccessService.Domain.Integrations.GitHub;
using AccessService.Domain.Onboarding;
using AccessService.Domain.TgJoinReminders;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Plans;

/// <summary>
/// L2 + cascade-тесты hard-delete плана (issue #417): guard по платным заказам / активным
/// грантам, каскадная зачистка всех ссылающихся таблиц, публикация <see cref="PlanHardDeleted"/>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class DeletePlanTests : AccessServiceTestsBase
{
    public DeletePlanTests(IntegrationTestsWebFactory factory) : base(factory) { }

    private async Task<Guid> CreateFullAccessPlanAsync(string slug = "full-access")
    {
        CreatePlanRequest req = new(
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

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync("/access/plans/", req);
        resp.EnsureSuccessStatusCode();
        Envelope<Guid>? env = await resp.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(env);
        return env!.Result;
    }

    private async Task<Guid> CreateCoursePlanAsync(Guid courseId, string slug = "course-plan")
    {
        CreatePlanRequest req = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: slug,
            DisplayName: "Курс-план",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: 150_000,
            Currency: "RUB",
            CourseIds: [courseId],
            DisplayOrder: 0);

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync("/access/plans/", req);
        resp.EnsureSuccessStatusCode();
        Envelope<Guid>? env = await resp.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(env);
        return env!.Result;
    }

    [Fact]
    public async Task Author_deletes_empty_plan_cascades_all_references_and_publishes_event()
    {
        Guid courseId = Guid.NewGuid();
        Guid planId = await CreateCoursePlanAsync(courseId); // создаёт plan + plan_courses row

        // Засеиваем все ссылающиеся таблицы, которые НЕ блокируют удаление:
        // invite_link + invite_redemption, REVOKED grant and the remaining plan projections.
        await ExecuteInDbAsync(async db =>
        {
            InviteLink invite = InviteLink.Create(
                planId, CurrentUserId, multiUse: true, maxUses: null, expiresAt: null, label: "promo");
            db.InviteLinks.Add(invite);

            PlanGrant revokedGrant = PlanGrant.Create(
                Guid.NewGuid(), planId, PlanGrantSource.ADMIN_GRANT, sourceRef: null);
            revokedGrant.Revoke(CurrentUserId, "test");
            db.PlanGrants.Add(revokedGrant);

            db.InviteRedemptions.Add(
                new InviteRedemption(invite.Id, revokedGrant.UserId, revokedGrant.Id, ipHash: null));

            db.UserPlanOnboardings.Add(
                UserPlanOnboarding.Start(Guid.NewGuid(), planId, DateTimeOffset.UtcNow));

            db.PlanPinnedMaterials.Add(
                PlanPinnedMaterial.Create(
                    planId, Guid.NewGuid(), note: null, SortKey.Initial(), DateTimeOffset.UtcNow).Value);

            db.GithubOrgInvitations.Add(
                GithubOrgInvitation.CreatePending(
                    planId, Guid.NewGuid(), "octocat", "my-org", githubInvitationId: 12345, DateTimeOffset.UtcNow));

            db.TgJoinReminders.Add(TgJoinReminder.Create(
                revokedGrant.UserId,
                planId,
                revokedGrant.Id,
                DateTimeOffset.UtcNow));

            await db.SaveChangesAsync();
        });

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/access/plans/{planId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        Assert.False(envelope!.IsError);
        Assert.Equal(planId, envelope.Result);

        await ExecuteInDbAsync(async db =>
        {
            Assert.False(await db.Plans.AnyAsync(p => p.Id == planId));
            Assert.False(await db.PlanCourses.AnyAsync(c => c.PlanId == planId));
            Assert.False(await db.InviteLinks.AnyAsync(i => i.PlanId == planId));
            Assert.False(await db.PlanGrants.AnyAsync(g => g.PlanId == planId));
            Assert.False(await db.Orders.AnyAsync(o => o.PlanId == planId));
            Assert.False(await db.UserPlanOnboardings.AnyAsync(o => o.PlanId == planId));
            Assert.False(await db.PlanPinnedMaterials.AnyAsync(p => p.PlanId == planId));
            Assert.False(await db.GithubOrgInvitations.AnyAsync(g => g.PlanId == planId));
            Assert.False(await db.TgJoinReminders.AnyAsync(r => r.PlanId == planId));
            // invite_redemptions has no plan_id and is cleaned through its references.
            Assert.Empty(await db.InviteRedemptions.ToListAsync());
        });

        PlanHardDeleted published = OutboxCollector.OfType<PlanHardDeleted>().Single();
        Assert.Equal(planId, published.PlanId);
        Assert.Equal(CurrentUserId, published.AuthorId);
    }

    [Fact]
    public async Task Delete_blocked_when_plan_has_paid_order()
    {
        Guid planId = await CreateFullAccessPlanAsync();

        await ExecuteInDbAsync(async db =>
        {
            Order order = Order.Create(Guid.NewGuid(), planId, 990_000, "RUB").Value;
            order.MarkPaid("ext-ref-paid");
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/access/plans/{planId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.True(envelope!.IsError);
        Assert.Contains(
            envelope.Error!.Messages,
            m => string.Equals(m.Code, "plan.delete.has_paid_orders", StringComparison.Ordinal));

        Assert.True(await ExecuteInDbAsync(db => db.Plans.AnyAsync(p => p.Id == planId)));
        Assert.Empty(OutboxCollector.OfType<PlanHardDeleted>());
    }

    [Fact]
    public async Task Delete_blocked_when_plan_has_refunded_order()
    {
        Guid planId = await CreateFullAccessPlanAsync();

        await ExecuteInDbAsync(async db =>
        {
            Order order = Order.Create(Guid.NewGuid(), planId, 990_000, "RUB").Value;
            order.MarkPaid("ext-ref-refunded");
            order.Refund("chargeback");
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/access/plans/{planId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.Contains(
            envelope!.Error!.Messages,
            m => string.Equals(m.Code, "plan.delete.has_paid_orders", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Delete_blocked_when_plan_has_active_grant()
    {
        Guid planId = await CreateFullAccessPlanAsync();

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = PlanGrant.Create(
                Guid.NewGuid(), planId, PlanGrantSource.ADMIN_GRANT, sourceRef: null);
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/access/plans/{planId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.Contains(
            envelope!.Error!.Messages,
            m => string.Equals(m.Code, "plan.delete.has_active_grants", StringComparison.Ordinal));

        Assert.True(await ExecuteInDbAsync(db => db.Plans.AnyAsync(p => p.Id == planId)));
        Assert.Empty(OutboxCollector.OfType<PlanHardDeleted>());
    }

    [Fact]
    public async Task Pending_order_blocks_delete_and_preserves_payment_audit()
    {
        Guid planId = await CreateFullAccessPlanAsync();

        await ExecuteInDbAsync(async db =>
        {
            Order order = Order.Create(Guid.NewGuid(), planId, 990_000, "RUB").Value;
            db.Orders.Add(order);
            db.OrderEvents.Add(OrderEvent.Record(order.Id, OrderEventType.INIT_CALLED));
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/access/plans/{planId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(
            envelope!.Error!.Messages,
            message => string.Equals(message.Code, "plan.delete.has_orders", StringComparison.Ordinal));

        await ExecuteInDbAsync(async db =>
        {
            Assert.True(await db.Plans.AnyAsync(p => p.Id == planId));
            Assert.True(await db.Orders.AnyAsync(o => o.PlanId == planId));
            Assert.True(await db.OrderEvents.AnyAsync());
        });
    }

    [Fact]
    public async Task Database_rejects_order_and_grant_for_missing_plan()
    {
        await ExecuteInDbAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.Orders.Add(Order.Create(
                    Guid.NewGuid(), Guid.NewGuid(), 100_000, "RUB").Value);
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            finally
            {
                await transaction.RollbackAsync();
            }
        });

        await ExecuteInDbAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.PlanGrants.Add(PlanGrant.Create(
                    Guid.NewGuid(), Guid.NewGuid(), PlanGrantSource.ADMIN_GRANT, sourceRef: null));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }
            finally
            {
                await transaction.RollbackAsync();
            }
        });
    }

    [Fact]
    public async Task Foreign_author_cannot_delete_someone_elses_plan()
    {
        Guid planId = await CreateFullAccessPlanAsync();

        AuthenticateAs("platform-author", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/access/plans/{planId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.Contains(
            envelope!.Error!.Messages,
            m => string.Equals(m.Code, "access.denied", StringComparison.Ordinal));

        Assert.True(await ExecuteInDbAsync(db => db.Plans.AnyAsync(p => p.Id == planId)));
    }

    [Fact]
    public async Task Returns_404_for_nonexistent_plan()
    {
        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/access/plans/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.NotNull(envelope);
        Assert.Contains(
            envelope!.Error!.Messages,
            m => string.Equals(m.Code, "plan.not.found", StringComparison.Ordinal));
    }
}
