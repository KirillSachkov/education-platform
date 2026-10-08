using AccessService.Contracts.Billing;
using AccessService.Core.Features.Billing.UseCases;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class PaymentWebhookHandlerTests : AccessServiceTestsBase
{
    public PaymentWebhookHandlerTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task PAID_creates_grant_with_source_ref_to_order()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        Plan plan = await CreateLifetimePlanAsync(authorId);
        Guid orderId = await CreateOrderAsync(userId, plan.Id);

        PaymentWebhookHandler handler = ResolveHandler();
        UnitResult<Error> result = await handler.Handle(
            new PaymentWebhookRequest(orderId, "ext-123", "PAID", null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);

        // Verify grant in DB has SourceRef = order.Id (новая семантика #85 fix).
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant? grant = await db.PlanGrants.AsNoTracking()
                .FirstOrDefaultAsync(g => g.UserId == userId && g.PlanId == plan.Id);
            Assert.NotNull(grant);
            Assert.Equal(PlanGrantSource.PURCHASE, grant!.Source);
            Assert.Equal(orderId, grant.SourceRef);
        });
    }

    [Fact]
    public async Task REFUNDED_publishes_PlanGrantRevoked_for_recalc()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        Plan plan = await CreateLifetimePlanAsync(authorId);
        Guid orderId = await CreateOrderAsync(userId, plan.Id);

        // Шаг 1: PAID → создаёт grant.
        PaymentWebhookHandler handler1 = ResolveHandler();
        UnitResult<Error> paidResult = await handler1.Handle(
            new PaymentWebhookRequest(orderId, "ext-456", "PAID", null),
            CancellationToken.None);
        Assert.True(paidResult.IsSuccess);

        // Шаг 2: REFUNDED — должен publish'ить PlanGrantRevoked, иначе #79 recalc
        // не сработает и Redis-теги останутся.
        PaymentWebhookHandler handler2 = ResolveHandler();
        UnitResult<Error> refundResult = await handler2.Handle(
            new PaymentWebhookRequest(orderId, "ext-456", "REFUNDED", "chargeback"),
            CancellationToken.None);
        Assert.True(refundResult.IsSuccess);

        // Состояние БД: order REFUNDED, grant REVOKED. PlanGrantRevoked event
        // протекает через outbox при SaveChanges — verify в БД, а не через
        // tracker (Wolverine outbox в тестах ставится в paused state).
        await ExecuteInDbAsync(async db =>
        {
            Order? order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId);
            Assert.NotNull(order);
            Assert.Equal(OrderStatus.REFUNDED, order!.Status);

            PlanGrant? grant = await db.PlanGrants.AsNoTracking()
                .FirstOrDefaultAsync(g => g.UserId == userId && g.PlanId == plan.Id);
            Assert.NotNull(grant);
            Assert.Equal(PlanGrantStatus.REVOKED, grant!.Status);
        });
    }

    [Fact]
    public async Task REFUNDED_revokes_admin_grant_issued_for_the_order()
    {
        // #414: refund должен снять доступ независимо от способа выдачи grant'а. Здесь
        // saccess выдал ADMIN_GRANT на order (клиент заплатил вне системы), затем пришёл
        // REFUNDED — grant обязан стать REVOKED.
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        Plan plan = await CreateLifetimePlanAsync(authorId);

        // Order сразу в PAID (Refund требует PAID-статус) с привязанным external ref.
        Order order = Order.Create(userId, plan.Id, amountCents: 100_000, currency: "RUB").Value;
        order.AttachExternalRef("ext-admin");
        order.MarkPaid("ext-admin");
        // ADMIN_GRANT grant на этот order (как grant-manually выдаёт).
        PlanGrant adminGrant = PlanGrant.Create(
            userId, plan.Id, PlanGrantSource.ADMIN_GRANT, sourceRef: order.Id);
        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(order);
            db.PlanGrants.Add(adminGrant);
            await db.SaveChangesAsync();
        });

        PaymentWebhookHandler handler = ResolveHandler();
        UnitResult<Error> refund = await handler.Handle(
            new PaymentWebhookRequest(order.Id, "ext-admin", "REFUNDED", "chargeback"),
            CancellationToken.None);
        Assert.True(refund.IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.REFUNDED, updated.Status);

            PlanGrant grant = await db.PlanGrants.AsNoTracking().FirstAsync(g => g.Id == adminGrant.Id);
            Assert.Equal(PlanGrantStatus.REVOKED, grant.Status);
        });

        // Self-consume recalc handler (#79) питается от PlanGrantRevoked — verify publish.
        Assert.Single(OutboxCollector.OfType<PlanGrantRevoked>(), e => e.GrantId == adminGrant.Id);
    }

    [Fact]
    public async Task PAID_idempotent_on_duplicate_external_ref()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();

        Plan plan = await CreateLifetimePlanAsync(authorId);
        Guid orderId = await CreateOrderAsync(userId, plan.Id);

        PaymentWebhookHandler handler = ResolveHandler();

        UnitResult<Error> first = await handler.Handle(
            new PaymentWebhookRequest(orderId, "ext-dup", "PAID", null), CancellationToken.None);
        UnitResult<Error> second = await handler.Handle(
            new PaymentWebhookRequest(orderId, "ext-dup", "PAID", null), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        // Только ОДИН grant создан, несмотря на 2 webhook hit'а.
        await ExecuteInDbAsync(async db =>
        {
            int grantCount = await db.PlanGrants.AsNoTracking()
                .CountAsync(g => g.UserId == userId && g.PlanId == plan.Id);
            Assert.Equal(1, grantCount);
        });
    }

    [Fact]
    public async Task PAID_records_grant_issued_audit_event()
    {
        // #443: успешная выдача grant'а после оплаты пишет GRANT_ISSUED audit-row.
        await Factory.ResetDatabaseAsync();
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Plan plan = await CreateLifetimePlanAsync(authorId);
        Guid orderId = await CreateOrderAsync(userId, plan.Id);

        PaymentWebhookHandler handler = ResolveHandler();
        UnitResult<Error> result = await handler.Handle(
            new PaymentWebhookRequest(orderId, "ext-grant-issued", "PAID", null), CancellationToken.None);
        Assert.True(result.IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            int issued = await db.OrderEvents.AsNoTracking()
                .CountAsync(e => e.OrderId == orderId && e.EventType == OrderEventType.GRANT_ISSUED);
            Assert.Equal(1, issued);
        });
    }

    [Fact]
    public async Task PAID_idempotent_records_single_grant_issued_event()
    {
        // Повторный webhook не плодит ни grant, ни GRANT_ISSUED audit-row.
        await Factory.ResetDatabaseAsync();
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Plan plan = await CreateLifetimePlanAsync(authorId);
        Guid orderId = await CreateOrderAsync(userId, plan.Id);

        PaymentWebhookHandler handler = ResolveHandler();
        await handler.Handle(new PaymentWebhookRequest(orderId, "ext-dup-grant", "PAID", null), CancellationToken.None);
        await handler.Handle(new PaymentWebhookRequest(orderId, "ext-dup-grant", "PAID", null), CancellationToken.None);

        await ExecuteInDbAsync(async db =>
        {
            int issued = await db.OrderEvents.AsNoTracking()
                .CountAsync(e => e.OrderId == orderId && e.EventType == OrderEventType.GRANT_ISSUED);
            Assert.Equal(1, issued);
            int grants = await db.PlanGrants.AsNoTracking().CountAsync(g => g.SourceRef == orderId);
            Assert.Equal(1, grants);
        });
    }

    [Fact]
    public async Task PAID_with_missing_plan_marks_order_failed_and_records_grant_failed()
    {
        // #443: оплата прошла, но план удалён, пока заказ был PENDING — grant выпустить не из чего.
        // Терминальный GRANT_FAILED: заказ FAILED, audit-row для админки, грант не создаётся.
        await Factory.ResetDatabaseAsync();
        Guid userId = Guid.NewGuid();
        Guid orphanPlanId = Guid.NewGuid(); // нет Plan-строки в БД

        Guid orderId = await CreateLegacyOrphanOrderAsync(userId, orphanPlanId);

        PaymentWebhookHandler handler = ResolveHandler();
        UnitResult<Error> result = await handler.Handle(
            new PaymentWebhookRequest(orderId, "ext-grant-failed", "PAID", null), CancellationToken.None);
        Assert.True(result.IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderId);
            Assert.Equal(OrderStatus.FAILED, order.Status);

            int failed = await db.OrderEvents.AsNoTracking()
                .CountAsync(e => e.OrderId == orderId && e.EventType == OrderEventType.GRANT_FAILED);
            Assert.Equal(1, failed);

            int grants = await db.PlanGrants.AsNoTracking().CountAsync(g => g.SourceRef == orderId);
            Assert.Equal(0, grants);
        });
    }

    private PaymentWebhookHandler ResolveHandler()
    {
        IServiceScope scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PaymentWebhookHandler>();
    }

    private async Task<Plan> CreateLifetimePlanAsync(Guid authorId)
    {
        Plan plan = Plan.Create(
            authorId: authorId,
            tier: PlanTier.FULL_ALL,
            slug: PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Test plan").Value,
            courseIds: [],
            requestedCapabilities: null).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });

        return plan;
    }

    private async Task<Guid> CreateOrderAsync(Guid userId, Guid planId)
    {
        Order order = Order.Create(userId, planId, amountCents: 100_000, currency: "RUB").Value;
        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });
        return order.Id;
    }

    private async Task<Guid> CreateLegacyOrphanOrderAsync(Guid userId, Guid planId)
    {
        Order order = Order.Create(userId, planId, amountCents: 100_000, currency: "RUB").Value;
        await ExecuteInDbAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
        return order.Id;
    }
}
