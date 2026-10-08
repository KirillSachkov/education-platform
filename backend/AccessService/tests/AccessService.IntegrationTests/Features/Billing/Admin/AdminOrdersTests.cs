using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Billing.Admin;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing.Admin;

/// <summary>
/// Integration tests for F.1.5 admin tools (issue #102):
/// list / detail / resync / grant-manually / revoke-grant / refund-stub.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class AdminOrdersTests : AccessServiceTestsBase
{
    public AdminOrdersTests(IntegrationTestsWebFactory factory) : base(factory) { }

    // ─── GET /access/admin/orders ─────────────────────────────────────────

    [Fact]
    public async Task ListOrders_AdminWithSeededData_ReturnsItems()
    {
        Plan plan = await SeedPlanAsync();
        await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING);
        await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PAID);
        await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.FAILED);

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/admin/orders/?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<ListAdminOrdersResponse>? envelope =
            await resp.Content.ReadFromJsonAsync<Envelope<ListAdminOrdersResponse>>();
        Assert.NotNull(envelope);
        Assert.False(envelope!.IsError);
        ListAdminOrdersResponse body = envelope.Result!;
        Assert.True(body.Total >= 3);
        Assert.NotEmpty(body.Items);
        Assert.Equal(1, body.Page);
        Assert.Equal(20, body.PageSize);
    }

    [Fact]
    public async Task ListOrders_StatusFilter_OnlyMatchingStatusReturned()
    {
        Plan plan = await SeedPlanAsync();
        await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING);
        await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PAID);
        await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PAID);

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/admin/orders/?status=PAID");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<ListAdminOrdersResponse>? envelope =
            await resp.Content.ReadFromJsonAsync<Envelope<ListAdminOrdersResponse>>();
        Assert.NotNull(envelope);
        Assert.All(envelope!.Result!.Items, i => Assert.Equal("PAID", i.Status));
    }

    [Fact]
    public async Task ListOrders_Anonymous_Returns401()
    {
        RemoveAuthentication();
        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/admin/orders/");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task ListOrders_Participant_Returns403()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/admin/orders/");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task ListOrders_Moderator_AllowedToList()
    {
        AuthenticateAs("platform-moderator", Guid.NewGuid());
        HttpResponseMessage resp = await AppHttpClient.GetAsync("/access/admin/orders/");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ─── GET /access/admin/orders/{id} ────────────────────────────────────

    [Fact]
    public async Task GetOrderDetail_Existing_ReturnsOrderAndEvents()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING);
        await SeedOrderEventAsync(order.Id, OrderEventType.WEBHOOK_RECEIVED, "{}");
        await SeedOrderEventAsync(order.Id, OrderEventType.MARK_PAID, "{}");

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/access/admin/orders/{order.Id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<GetAdminOrderDetailResponse>? envelope =
            await resp.Content.ReadFromJsonAsync<Envelope<GetAdminOrderDetailResponse>>();
        Assert.NotNull(envelope);
        Assert.Equal(order.Id, envelope!.Result!.Order.OrderId);
        Assert.Equal(2, envelope.Result.Events.Count);
    }

    [Fact]
    public async Task GetOrderDetail_NotFound_Returns404()
    {
        AuthenticateAsAdmin();
        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/access/admin/orders/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetOrderDetail_ReturnsCorrelationIdOnOrderAndEvents()
    {
        // #443: detail отдаёт correlation_id на заказе И на каждом событии timeline.
        const string correlationId = "0123456789abcdef0123456789abcdef";
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.FAILED, correlationId: correlationId);
        await SeedOrderEventAsync(order.Id, OrderEventType.INIT_CALLED, "{}", correlationId);
        await SeedOrderEventAsync(order.Id, OrderEventType.INIT_FAILED, "{}", correlationId);

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/access/admin/orders/{order.Id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        GetAdminOrderDetailResponse body =
            (await resp.Content.ReadFromJsonAsync<Envelope<GetAdminOrderDetailResponse>>())!.Result!;
        Assert.Equal(correlationId, body.Order.CorrelationId);
        Assert.NotEmpty(body.Events);
        Assert.All(body.Events, e => Assert.Equal(correlationId, e.CorrelationId));
    }

    [Fact]
    public async Task ListOrders_CorrelationIdFilter_OnlyMatchingReturned()
    {
        // #443: фильтр по correlation_id отдаёт ровно заказы с этим trace_id.
        const string target = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string other = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        Plan plan = await SeedPlanAsync();
        Order matching = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING, correlationId: target);
        await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING, correlationId: other);
        await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING, correlationId: null);

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.GetAsync($"/access/admin/orders/?correlationId={target}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        ListAdminOrdersResponse body =
            (await resp.Content.ReadFromJsonAsync<Envelope<ListAdminOrdersResponse>>())!.Result!;
        Assert.Equal(1, body.Total);
        AdminOrderSummary item = Assert.Single(body.Items);
        Assert.Equal(matching.Id, item.OrderId);
        Assert.Equal(target, item.CorrelationId);
    }

    // ─── POST /access/admin/orders/{id}/resync ────────────────────────────

    [Fact]
    public async Task ResyncOrder_PendingConfirmedAtTBank_MarksPaidAndCreatesGrant()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedPendingOrderWithExternalRefAsync(Guid.NewGuid(), plan.Id, "ext-resync");

        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
            Amount = order.AmountCents,
        };

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/orders/{order.Id}/resync", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<ResyncOrderResponse>? envelope =
            await resp.Content.ReadFromJsonAsync<Envelope<ResyncOrderResponse>>();
        Assert.NotNull(envelope);
        Assert.Equal("PENDING", envelope!.Result!.PreviousStatus);
        Assert.Equal("PAID", envelope.Result.NewStatus);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, updated.Status);

            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g =>
                g.UserId == order.UserId && g.SourceRef == order.Id);
            Assert.Equal(1, grantCount);
        });
    }

    [Fact]
    public async Task ResyncOrder_ReconciliationExpiredConfirmedAtTBank_RecoversPurchase()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedFailedOrderWithExternalRefAsync(
            Guid.NewGuid(), plan.Id, "ext-expired-resync", "reconciliation_expired");

        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
            Amount = order.AmountCents,
        };

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/orders/{order.Id}/resync", content: null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<ResyncOrderResponse>? envelope =
            await resp.Content.ReadFromJsonAsync<Envelope<ResyncOrderResponse>>();
        Assert.Equal("FAILED", envelope!.Result!.PreviousStatus);
        Assert.Equal("PAID", envelope.Result.NewStatus);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, updated.Status);
            Assert.Null(updated.FailureReason);
            Assert.NotNull(updated.PaidAt);

            PlanGrant grant = await db.PlanGrants.AsNoTracking()
                .SingleAsync(g => g.UserId == order.UserId && g.PlanId == plan.Id);
            Assert.Equal(PlanGrantSource.PURCHASE, grant.Source);
            Assert.Equal(order.Id, grant.SourceRef);
            Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
        });
    }

    [Fact]
    public async Task ResyncOrder_FailedForAnotherReason_Returns400WithoutProviderCall()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedFailedOrderWithExternalRefAsync(
            Guid.NewGuid(), plan.Id, "ext-declined-resync", "provider_declined");

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/orders/{order.Id}/resync", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Empty(Factory.TBankClient.GetStateCalls);
    }

    [Theory]
    [InlineData("payment_id")]
    [InlineData("order_id")]
    [InlineData("amount")]
    public async Task ResyncOrder_ProviderSnapshotMismatch_FailsClosed(string mismatch)
    {
        Plan plan = await SeedPlanAsync();
        const string externalRef = "ext-mismatch-resync";
        Order order = await SeedFailedOrderWithExternalRefAsync(
            Guid.NewGuid(), plan.Id, externalRef, "reconciliation_expired");

        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = mismatch == "payment_id" ? "another-payment" : paymentId,
            OrderId = mismatch == "order_id" ? Guid.NewGuid().ToString() : order.Id.ToString(),
            Amount = mismatch == "amount" ? order.AmountCents + 1 : order.AmountCents,
        };

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/orders/{order.Id}/resync", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Equal(externalRef, Assert.Single(Factory.TBankClient.GetStateCalls));

        await ExecuteInDbAsync(async db =>
        {
            Order unchanged = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.FAILED, unchanged.Status);
            Assert.Equal("reconciliation_expired", unchanged.FailureReason);
            Assert.Null(unchanged.PaidAt);
            Assert.Equal(0, await db.PlanGrants.AsNoTracking().CountAsync(g => g.UserId == order.UserId));
        });
    }

    [Fact]
    public async Task ResyncOrder_NotPending_Returns400()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PAID);

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsync(
            $"/access/admin/orders/{order.Id}/resync", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ─── POST /access/admin/orders/{id}/grant-manually ────────────────────

    [Fact]
    public async Task GrantManually_ValidReason_CreatesAdminGrantWithoutChangingOrder()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING);

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/orders/{order.Id}/grant-manually",
            new GrantManuallyRequest("Bank transfer received outside system"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        Envelope<GrantManuallyResponse>? envelope =
            await resp.Content.ReadFromJsonAsync<Envelope<GrantManuallyResponse>>();
        Assert.NotNull(envelope);
        Assert.NotEqual(Guid.Empty, envelope!.Result!.GrantId);

        await ExecuteInDbAsync(async db =>
        {
            // Order status НЕ изменился.
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PENDING, updated.Status);

            // Grant создан, source = ADMIN_GRANT, source_ref = order.Id.
            PlanGrant grant = await db.PlanGrants.AsNoTracking().FirstAsync(g => g.SourceRef == order.Id);
            Assert.Equal(PlanGrantSource.ADMIN_GRANT, grant.Source);
            Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
            Assert.Equal(order.UserId, grant.UserId);

            // Audit row.
            int auditCount = await db.OrderEvents.AsNoTracking().CountAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.MANUAL_GRANT_BY_ADMIN);
            Assert.Equal(1, auditCount);
        });
    }

    [Fact]
    public async Task GrantManually_EmptyReason_Returns400()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING);

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/orders/{order.Id}/grant-manually",
            new GrantManuallyRequest(string.Empty));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task GrantManually_NonAdmin_Returns403()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING);

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/orders/{order.Id}/grant-manually",
            new GrantManuallyRequest("test"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ─── POST /access/admin/orders/{id}/revoke-grant ──────────────────────

    [Fact]
    public async Task RevokeGrant_ExistingActiveGrant_Revokes()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PAID);

        // Pre-existing active PURCHASE grant from order.
        PlanGrant grant = PlanGrant.Create(order.UserId, plan.Id, PlanGrantSource.PURCHASE, sourceRef: order.Id);
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/orders/{order.Id}/revoke-grant",
            new RevokeGrantRequest("Terms violation"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant updated = await db.PlanGrants.AsNoTracking().FirstAsync(g => g.Id == grant.Id);
            Assert.Equal(PlanGrantStatus.REVOKED, updated.Status);
            Assert.Equal("Terms violation", updated.RevokeReason);

            int auditCount = await db.OrderEvents.AsNoTracking().CountAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.MANUAL_REVOKE_BY_ADMIN);
            Assert.Equal(1, auditCount);
        });
    }

    [Fact]
    public async Task RevokeGrant_NoExistingGrant_Returns404()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PENDING);

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/orders/{order.Id}/revoke-grant",
            new RevokeGrantRequest("test"));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ─── POST /access/admin/orders/{id}/refund (stub) ─────────────────────

    [Fact]
    public async Task RefundOrder_Admin_Returns501NotImplemented()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PAID);

        AuthenticateAsAdmin();

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/orders/{order.Id}/refund",
            new RefundOrderRequest("test"));
        Assert.Equal(HttpStatusCode.NotImplemented, resp.StatusCode);
    }

    [Fact]
    public async Task RefundOrder_NonAdmin_Returns403()
    {
        Plan plan = await SeedPlanAsync();
        Order order = await SeedOrderAsync(Guid.NewGuid(), plan.Id, OrderStatus.PAID);

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/orders/{order.Id}/refund",
            new RefundOrderRequest("test"));
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private async Task<Plan> SeedPlanAsync(Guid? authorId = null)
    {
        Plan plan = Plan.Create(
            authorId: authorId ?? Guid.NewGuid(),
            tier: PlanTier.FULL_ALL,
            slug: PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Test plan").Value,
            courseIds: [], requestedCapabilities: null).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan;
    }

    private async Task<Order> SeedOrderAsync(
        Guid userId,
        Guid planId,
        OrderStatus targetStatus,
        string? failureReason = null,
        string? correlationId = null)
    {
        Order order = Order.Create(
            userId, planId, amountCents: 500_000, currency: "RUB", provider: "tbank",
            correlationId: correlationId).Value;

        switch (targetStatus)
        {
            case OrderStatus.PENDING:
                break;
            case OrderStatus.PAID:
                order.MarkPaid("ext-pid-" + Guid.NewGuid().ToString("N")[..8]);
                break;
            case OrderStatus.FAILED:
                order.MarkFailed(failureReason);
                break;
            case OrderStatus.REFUNDED:
                order.MarkPaid("ext-pid-" + Guid.NewGuid().ToString("N")[..8]);
                order.Refund(failureReason);
                break;
        }

        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });
        return order;
    }

    private async Task<Order> SeedPendingOrderWithExternalRefAsync(
        Guid userId,
        Guid planId,
        string externalRef)
    {
        Order order = Order.Create(userId, planId, amountCents: 500_000, currency: "RUB", provider: "tbank").Value;
        UnitResult<Error> attach = order.AttachExternalRef(externalRef);
        Assert.True(attach.IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });
        return order;
    }

    private async Task<Order> SeedFailedOrderWithExternalRefAsync(
        Guid userId,
        Guid planId,
        string externalRef,
        string failureReason)
    {
        Order order = Order.Create(
            userId, planId, amountCents: 500_000, currency: "RUB", provider: "tbank").Value;
        Assert.True(order.AttachExternalRef(externalRef).IsSuccess);
        Assert.True(order.MarkFailed(failureReason).IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });
        return order;
    }

    private async Task SeedOrderEventAsync(
        Guid orderId, OrderEventType type, string? payload = null, string? correlationId = null) =>
        await ExecuteInDbAsync(async db =>
        {
            db.OrderEvents.Add(OrderEvent.Record(orderId, type, payload, correlationId: correlationId));
            await db.SaveChangesAsync();
        });
}
