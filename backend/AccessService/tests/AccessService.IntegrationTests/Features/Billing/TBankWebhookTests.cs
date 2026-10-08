using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AccessService.Core.Database;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

/// <summary>
/// Integration tests for <c>POST /access/webhooks/tbank/</c> (Phase F.1.2.2, issue #102).
/// Sends realistic T-Bank notifications (with valid sha256 token) and asserts the
/// resulting Order state + PlanGrant side-effects. Token computation goes through
/// the same <see cref="TBankSignature"/> helper that the production endpoint uses.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class TBankWebhookTests : AccessServiceTestsBase
{
    private const string TBANK_PASSWORD = "test-password";
    private const string TBANK_TERMINAL_KEY = "test-terminal";
    private const int DEFAULT_AMOUNT_CENTS = 500_000;

    public TBankWebhookTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task POST_webhook_Confirmed_MarksOrderPaidAndIssuesPlanGrant()
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        TBankNotification notification = BuildNotification(order, paymentId, "CONFIRMED");

        HttpResponseMessage response = await PostWebhookAsync(notification);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, updated.Status);

            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g =>
                g.UserId == order.UserId && g.PlanId == order.PlanId
                && g.Source == PlanGrantSource.PURCHASE && g.SourceRef == order.Id
                && g.Status == PlanGrantStatus.ACTIVE);
            Assert.Equal(1, grantCount);
        });
    }

    [Fact]
    public async Task POST_webhook_ProcessedNotification_ReturnsExactTBankAcknowledgement()
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        TBankNotification notification = BuildNotification(order, paymentId, "CONFIRMED");

        HttpResponseMessage response = await PostWebhookAsync(notification);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task POST_webhook_AuditPayloadDoesNotPersistPaymentCredentials()
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        TBankNotification notification = BuildNotification(order, paymentId, "CONFIRMED");
        notification.Pan = "430000******0777";
        notification.ExpDate = "1230";
        notification.RebillId = "rebill-secret";

        HttpResponseMessage response = await PostWebhookAsync(notification);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            string payload = (await db.OrderEvents.AsNoTracking().FirstAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.WEBHOOK_RECEIVED)).PayloadJson!;

            Assert.DoesNotContain("Token", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("RebillId", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("rebill-secret", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("430000", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("1230", payload, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task POST_webhook_AuthorizedRebillAndConfirmed_PersistsRecurringGrantRegardlessOfDeliveryOrder(
        bool authorizedFirst)
    {
        (Plan plan, Order order, string paymentId) = await SeedSubscriptionAndPendingOrderAsync();
        TBankNotification authorized = BuildNotification(order, paymentId, "AUTHORIZED");
        authorized.RebillId = "rebill-authorized";
        TBankNotification confirmed = BuildNotification(order, paymentId, "CONFIRMED");

        if (authorizedFirst)
        {
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(authorized)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(confirmed)).StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(confirmed)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(authorized)).StatusCode);
        }

        await ExecuteInDbAsync(async db =>
        {
            Order updatedOrder = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g =>
                g.UserId == order.UserId && g.PlanId == plan.Id && g.SourceRef == order.Id);

            Assert.Equal(OrderStatus.PAID, updatedOrder.Status);
            Assert.Equal("rebill-authorized", updatedOrder.RebillId);
            Assert.Equal("rebill-authorized", grant.RebillId);
            Assert.Equal(order.UserId.ToString(), grant.CustomerKey);
            Assert.Equal(
                SubscriptionRenewalPolicy.FirstChargeAt(grant.ExpiresAt!.Value),
                grant.NextChargeAt);
        });
    }

    [Fact]
    public async Task POST_webhook_InvalidToken_Returns401_NoStateChange()
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        TBankNotification notification = BuildNotification(order, paymentId, "CONFIRMED");
        notification.Token = "0000000000000000000000000000000000000000000000000000000000000000";

        HttpResponseMessage response = await PostWebhookAsync(notification, computeToken: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PENDING, updated.Status);
            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g => g.SourceRef == order.Id);
            Assert.Equal(0, grantCount);
        });
    }

    [Fact]
    public async Task POST_webhook_AmountMismatch_MarksFailed_Returns200_NoGrant()
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        TBankNotification notification = BuildNotification(order, paymentId, "CONFIRMED");
        notification.Amount = 100; // doesn't match Order.AmountCents

        HttpResponseMessage response = await PostWebhookAsync(notification);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.FAILED, updated.Status);
            Assert.Contains("amount_mismatch", updated.FailureReason ?? string.Empty, StringComparison.Ordinal);

            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g => g.SourceRef == order.Id);
            Assert.Equal(0, grantCount);
        });
    }

    [Fact]
    public async Task POST_webhook_Confirmed_DuplicateDelivery_Idempotent_OneGrant()
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        TBankNotification notification = BuildNotification(order, paymentId, "CONFIRMED");

        HttpResponseMessage r1 = await PostWebhookAsync(notification);
        // Re-build for the second send so token is recomputed cleanly (request body is read once).
        TBankNotification notification2 = BuildNotification(order, paymentId, "CONFIRMED");
        HttpResponseMessage r2 = await PostWebhookAsync(notification2);

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, updated.Status);

            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g =>
                g.UserId == order.UserId && g.PlanId == order.PlanId
                && g.Source == PlanGrantSource.PURCHASE && g.SourceRef == order.Id);
            Assert.Equal(1, grantCount);
        });
    }

    [Fact]
    public async Task POST_webhook_ConfirmedAfterReconciliationExpiry_RecoversPaidOrderAndIssuesOneGrant()
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        await ExecuteInDbAsync(async db =>
        {
            Order tracked = await db.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.True(tracked.MarkFailed("reconciliation_expired").IsSuccess);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage first = await PostWebhookAsync(
            BuildNotification(order, paymentId, "CONFIRMED"));
        HttpResponseMessage duplicate = await PostWebhookAsync(
            BuildNotification(order, paymentId, "CONFIRMED"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, updated.Status);
            Assert.Equal(paymentId, updated.ExternalProviderRef);
            Assert.NotNull(updated.PaidAt);
            Assert.Null(updated.FailureReason);

            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g =>
                g.UserId == order.UserId
                && g.PlanId == order.PlanId
                && g.SourceRef == order.Id);
            Assert.Equal(PlanGrantSource.PURCHASE, grant.Source);
            Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
        });
    }

    [Fact]
    public async Task POST_webhook_ConfirmedAfterReconciliationExpiry_WithSameOrderAdminGrant_RecordsManualReview()
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        PlanGrant adminGrant = PlanGrant.Create(
            order.UserId,
            order.PlanId,
            PlanGrantSource.ADMIN_GRANT,
            sourceRef: order.Id);
        await ExecuteInDbAsync(async db =>
        {
            Order tracked = await db.Orders.FirstAsync(o => o.Id == order.Id);
            Assert.True(tracked.MarkFailed("reconciliation_expired").IsSuccess);
            db.PlanGrants.Add(adminGrant);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await PostWebhookAsync(
            BuildNotification(order, paymentId, "CONFIRMED"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, updated.Status);
            Assert.Null(updated.FailureReason);

            PlanGrant onlyGrant = await db.PlanGrants.AsNoTracking().SingleAsync(g =>
                g.UserId == order.UserId && g.PlanId == order.PlanId);
            Assert.Equal(adminGrant.Id, onlyGrant.Id);
            Assert.Equal(PlanGrantSource.ADMIN_GRANT, onlyGrant.Source);
            Assert.Equal(order.Id, onlyGrant.SourceRef);

            OrderEvent audit = await db.OrderEvents.AsNoTracking().SingleAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.GRANT_FAILED);
            using JsonDocument payload = JsonDocument.Parse(audit.PayloadJson!);
            Assert.Equal("active_grant_exists", payload.RootElement.GetProperty("reason").GetString());
            Assert.Equal(adminGrant.Id, payload.RootElement.GetProperty("existingGrantId").GetGuid());
            Assert.Equal("manual_review", payload.RootElement.GetProperty("action").GetString());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task POST_webhook_Confirmed_WithDifferentActiveGrant_RecordsManualReview(
        bool reconciliationExpired)
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        Guid replacementOrderId = Guid.NewGuid();
        PlanGrant replacementGrant = PlanGrant.Create(
            order.UserId,
            order.PlanId,
            PlanGrantSource.PURCHASE,
            sourceRef: replacementOrderId,
            pricePaidCents: order.AmountCents);
        await ExecuteInDbAsync(async db =>
        {
            if (reconciliationExpired)
            {
                Order tracked = await db.Orders.FirstAsync(o => o.Id == order.Id);
                Assert.True(tracked.MarkFailed("reconciliation_expired").IsSuccess);
            }
            db.PlanGrants.Add(replacementGrant);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage response = await PostWebhookAsync(
            BuildNotification(order, paymentId, "CONFIRMED"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, updated.Status);
            Assert.Null(updated.FailureReason);

            PlanGrant onlyGrant = await db.PlanGrants.AsNoTracking().SingleAsync(g =>
                g.UserId == order.UserId && g.PlanId == order.PlanId);
            Assert.Equal(replacementGrant.Id, onlyGrant.Id);
            Assert.Equal(replacementOrderId, onlyGrant.SourceRef);

            OrderEvent audit = await db.OrderEvents.AsNoTracking().SingleAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.GRANT_FAILED);
            using JsonDocument payload = JsonDocument.Parse(audit.PayloadJson!);
            Assert.Equal("active_grant_exists", payload.RootElement.GetProperty("reason").GetString());
            Assert.Equal(replacementGrant.Id, payload.RootElement.GetProperty("existingGrantId").GetGuid());
            Assert.Equal("manual_review", payload.RootElement.GetProperty("action").GetString());
        });
    }

    [Fact]
    public async Task POST_webhook_Rejected_MarksFailed_NoGrant()
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        TBankNotification notification = BuildNotification(order, paymentId, "REJECTED", success: false);
        notification.ErrorCode = "1051";

        HttpResponseMessage response = await PostWebhookAsync(notification);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.FAILED, updated.Status);
            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g => g.SourceRef == order.Id);
            Assert.Equal(0, grantCount);
        });
    }

    [Theory]
    [InlineData("NEW")]
    [InlineData("FORM_SHOWED")]
    [InlineData("AUTHORIZING")]
    [InlineData("CONFIRMING")]
    public async Task POST_webhook_Intermediate_NoStateChange_Returns200(string tbankStatus)
    {
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        TBankNotification notification = BuildNotification(order, paymentId, tbankStatus, success: false);

        HttpResponseMessage response = await PostWebhookAsync(notification);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PENDING, updated.Status);
            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g => g.SourceRef == order.Id);
            Assert.Equal(0, grantCount);
        });
    }

    [Fact]
    public async Task POST_webhook_PartialRefunded_NoStateChange_WritesAuditRow_Returns200()
    {
        // #414: частичные возвраты не поддерживаются — Order не меняет статус (доступ
        // сохранён), но пишется audit-row PARTIAL_REFUND_IGNORED для расследования.
        (Plan _, Order order, string paymentId) = await SeedPlanAndPendingOrderAsync();
        // Order должен быть PAID, чтобы PARTIAL_REFUNDED был реалистичным сценарием.
        await ExecuteInDbAsync(async db =>
        {
            Order tracked = await db.Orders.FirstAsync(o => o.Id == order.Id);
            tracked.MarkPaid(paymentId);
            await db.SaveChangesAsync();
        });

        TBankNotification notification = BuildNotification(order, paymentId, "PARTIAL_REFUNDED");

        HttpResponseMessage response = await PostWebhookAsync(notification);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            // Статус не изменился — доступ сохранён (PAID).
            Assert.Equal(OrderStatus.PAID, updated.Status);

            bool auditExists = await db.OrderEvents.AsNoTracking().AnyAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.PARTIAL_REFUND_IGNORED);
            Assert.True(auditExists);

            // grant'ов на order'е не было — refund не должен ничего отзывать.
            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g => g.SourceRef == order.Id);
            Assert.Equal(0, grantCount);
        });
    }

    [Fact]
    public async Task POST_webhook_RenewalRefund_ChargeLockBusy_RetriesBeforeMutation()
    {
        Plan plan = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.SUBSCRIPTION,
            slug: PlanSlug.Of($"subscription-refund-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Refund lock plan").Value,
            courseIds: [],
            requestedCapabilities: null,
            term: PlanTerm.Recurring(30)).Value;
        plan.UpdatePrice(DEFAULT_AMOUNT_CENTS, "RUB");

        Guid userId = Guid.NewGuid();
        DateTimeOffset previousExpiresAt = DateTimeOffset.UtcNow.AddDays(15);
        DateTimeOffset targetExpiresAt = previousExpiresAt.AddDays(30);
        PlanGrant grant = PlanGrant.Create(
            userId,
            plan.Id,
            PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(),
            expiresAt: targetExpiresAt,
            pricePaidCents: DEFAULT_AMOUNT_CENTS);
        Assert.True(grant.AttachRecurring(
            "rebill-refund-lock",
            userId.ToString(),
            SubscriptionRenewalPolicy.FirstChargeAt(targetExpiresAt)).IsSuccess);

        Order order = Order.CreateRenewal(
            userId,
            plan.Id,
            grant.Id,
            DEFAULT_AMOUNT_CENTS,
            "RUB",
            "rebill-refund-lock",
            provider: "tbank").Value;
        const string paymentId = "7777777777";
        Assert.True(order.AttachExternalRef(paymentId).IsSuccess);
        Assert.True(order.RecordRenewalPeriod(grant.Id, previousExpiresAt, targetExpiresAt).IsSuccess);
        Assert.True(order.MarkPaid(paymentId).IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            db.PlanGrants.Add(grant);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });

        await using AsyncServiceScope lockScope = Factory.Services.CreateAsyncScope();
        IOrdersRepository orders = lockScope.ServiceProvider.GetRequiredService<IOrdersRepository>();
        await using IAsyncDisposable heldLock = (await orders.TryAcquireRenewalLockAsync(
            grant.Id,
            CancellationToken.None))!;

        HttpResponseMessage blockedResponse = await PostWebhookAsync(
            BuildNotification(order, paymentId, "REFUNDED"));

        Assert.Equal(HttpStatusCode.InternalServerError, blockedResponse.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            Order unchangedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            PlanGrant unchangedGrant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(OrderStatus.PAID, unchangedOrder.Status);
            Assert.NotNull(unchangedGrant.ExpiresAt);
            Assert.Equal(
                targetExpiresAt,
                unchangedGrant.ExpiresAt.Value,
                TimeSpan.FromMicroseconds(1));
            Assert.NotNull(unchangedGrant.NextChargeAt);
        });

        await heldLock.DisposeAsync();
        HttpResponseMessage retryResponse = await PostWebhookAsync(
            BuildNotification(order, paymentId, "REFUNDED"));

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        await ExecuteInDbAsync(async db =>
        {
            Order refundedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            PlanGrant rolledBackGrant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(OrderStatus.REFUNDED, refundedOrder.Status);
            Assert.NotNull(rolledBackGrant.ExpiresAt);
            Assert.Equal(
                previousExpiresAt,
                rolledBackGrant.ExpiresAt.Value,
                TimeSpan.FromMicroseconds(1));
            Assert.Null(rolledBackGrant.NextChargeAt);
        });
    }

    [Fact]
    public async Task POST_webhook_OrderNotFound_Returns200_NoCrash()
    {
        // Synthesize a CONFIRMED notification for a non-existent OrderId.
        TBankNotification notification = new()
        {
            TerminalKey = TBANK_TERMINAL_KEY,
            OrderId = Guid.NewGuid().ToString(),
            Success = true,
            Status = "CONFIRMED",
            PaymentId = 9999999999,
            ErrorCode = "0",
            Amount = DEFAULT_AMOUNT_CENTS,
            Token = string.Empty,
        };

        HttpResponseMessage response = await PostWebhookAsync(notification);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task POST_webhook_BadOrderId_Returns200()
    {
        TBankNotification notification = new()
        {
            TerminalKey = TBANK_TERMINAL_KEY,
            OrderId = "not-a-guid",
            Success = true,
            Status = "CONFIRMED",
            PaymentId = 9999999999,
            ErrorCode = "0",
            Amount = DEFAULT_AMOUNT_CENTS,
            Token = string.Empty,
        };

        HttpResponseMessage response = await PostWebhookAsync(notification);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static TBankNotification BuildNotification(
        Order order, string paymentId, string status, bool success = true)
    {
        return new TBankNotification
        {
            TerminalKey = TBANK_TERMINAL_KEY,
            OrderId = order.Id.ToString(),
            Success = success,
            Status = status,
            PaymentId = long.Parse(paymentId, CultureInfo.InvariantCulture),
            ErrorCode = "0",
            Amount = order.AmountCents,
            Token = string.Empty,
        };
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(
        TBankNotification notification, bool computeToken = true)
    {
        if (computeToken)
            notification.Token = ComputeTBankToken(notification, TBANK_PASSWORD);
        return await AppHttpClient.PostAsJsonAsync("/access/webhooks/tbank/", notification);
    }

    /// <summary>
    /// Mirrors <c>TBankWebhookHandler.VerifyToken</c> field set: top-level scalars
    /// EXCLUDING <c>Token</c>, plus optional fields when non-empty. Sorted Ordinal +
    /// (Password, password_value) → SHA256 hex (см. <see cref="TBankSignature.ComputeToken"/>).
    /// </summary>
    private static string ComputeTBankToken(TBankNotification n, string password)
    {
        Dictionary<string, string> fields = new(StringComparer.Ordinal)
        {
            ["TerminalKey"] = n.TerminalKey,
            ["OrderId"] = n.OrderId,
            ["Success"] = n.Success ? "true" : "false",
            ["Status"] = n.Status,
            ["PaymentId"] = n.PaymentId.ToString(CultureInfo.InvariantCulture),
            ["Amount"] = n.Amount.ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrEmpty(n.ErrorCode)) fields["ErrorCode"] = n.ErrorCode;
        if (!string.IsNullOrEmpty(n.Pan)) fields["Pan"] = n.Pan;
        if (!string.IsNullOrEmpty(n.ExpDate)) fields["ExpDate"] = n.ExpDate;
        if (!string.IsNullOrEmpty(n.RebillId)) fields["RebillId"] = n.RebillId;
        return TBankSignature.ComputeToken(fields, password);
    }

    /// <summary>
    /// Direct-DB seed of a LIFETIME_ALL plan + PENDING order with a known PaymentId
    /// already attached as <c>ExternalProviderRef</c>. Mirrors the pattern from
    /// <c>PaymentWebhookHandlerTests.CreateLifetimePlanAsync</c> + a deterministic
    /// payment id so the webhook payload's PaymentId matches the order's external ref.
    /// </summary>
    private async Task<(Plan plan, Order order, string paymentId)> SeedPlanAndPendingOrderAsync(
        int amountCents = DEFAULT_AMOUNT_CENTS)
    {
        Guid authorId = Guid.NewGuid();
        Plan plan = Plan.Create(
            authorId: authorId,
            tier: PlanTier.FULL_ALL,
            slug: PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Test plan").Value,
            courseIds: [], requestedCapabilities: null).Value;

        Guid userId = Guid.NewGuid();
        Order order = Order.Create(userId, plan.Id, amountCents, "RUB", "tbank").Value;
        const string paymentId = "9999999999";
        UnitResult<Error> attachResult = order.AttachExternalRef(paymentId);
        Assert.True(attachResult.IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });

        return (plan, order, paymentId);
    }

    private async Task<(Plan plan, Order order, string paymentId)> SeedSubscriptionAndPendingOrderAsync()
    {
        Plan plan = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.SUBSCRIPTION,
            slug: PlanSlug.Of($"subscription-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Trainer Pro").Value,
            courseIds: [],
            requestedCapabilities: null,
            term: PlanTerm.Recurring(30)).Value;
        plan.UpdatePrice(DEFAULT_AMOUNT_CENTS, "RUB");

        Order order = Order.Create(
            Guid.NewGuid(), plan.Id, DEFAULT_AMOUNT_CENTS, "RUB", "tbank").Value;
        const string paymentId = "8888888888";
        Assert.True(order.AttachExternalRef(paymentId).IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });

        return (plan, order, paymentId);
    }
}
