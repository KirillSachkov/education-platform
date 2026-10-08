using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.Reconciliation;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using AccessService.Infrastructure.Postgres;
using AccessService.Web.Jobs;
using AuthService.Contracts;
using Core.Database;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

/// <summary>
/// Integration tests for <see cref="RecurringChargesSweeper"/> (#614, A2b) — autonomous
/// recurring-subscription auto-renewal. Mocks the provider via <see cref="FakeTBankClient"/>
/// and invokes <see cref="RecurringChargesSweeper.SweepOnceAsync"/> directly (the sweeper is a
/// BackgroundService not hosted in the Testing environment — see AccessService/CLAUDE.md).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class RecurringChargesSweeperTests : AccessServiceTestsBase
{
    private const int IntervalDays = 30;
    private const long PriceCents = 99_000;

    public RecurringChargesSweeperTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task SweepOnce_DueRecurringGrant_InitChargeSuccess_RenewsAndPublishesRenewed()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(userId, plan.Id, rebillId: "rebill-ok", failureCount: 0);
        Factory.AuthClient.UsersById[userId] = new AuthUserLookupDto(
            userId, "Subscriber", "subscriber", "subscriber@example.test", null);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        int renewed = await RunSweeperAsync();
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.Equal(1, renewed);

        // Provider was hit: Init (renewal payment) + Charge (saved card via RebillId).
        TBankInitRequest initCall = Assert.Single(Factory.TBankClient.InitCalls);
        Assert.Equal("R", initCall.DATA!["OperationInitiatorType"]);
        Assert.NotNull(initCall.Receipt);
        Assert.Equal("subscriber@example.test", initCall.Receipt.Email);
        Assert.Equal(PriceCents, Assert.Single(initCall.Receipt.Items).Amount);
        var chargeCall = Assert.Single(Factory.TBankClient.ChargeCalls);
        Assert.Equal("rebill-ok", chargeCall.RebillId);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant updated = await db.PlanGrants.AsNoTracking().FirstAsync(g => g.Id == grant.Id);
            Assert.Equal(PlanGrantStatus.ACTIVE, updated.Status);
            Assert.Equal(0, updated.ChargeFailureCount);
            // ExpiresAt extended by one interval; next charge follows T-24h (#746).
            Assert.NotNull(updated.ExpiresAt);
            Assert.Equal(
                SubscriptionRenewalPolicy.FirstChargeAt(updated.ExpiresAt!.Value),
                updated.NextChargeAt);
            Assert.InRange(
                updated.ExpiresAt!.Value,
                before.AddDays(IntervalDays).AddSeconds(-10),
                after.AddDays(IntervalDays).AddSeconds(10));

            // A RENEWAL order was created and marked PAID.
            Order renewalOrder = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.PlanId == plan.Id && o.ChargeType == OrderChargeType.RENEWAL);
            Assert.Equal(OrderStatus.PAID, renewalOrder.Status);
            Assert.Equal("rebill-ok", renewalOrder.RebillId);
        });

        PlanGrantRenewed published = Assert.Single(OutboxCollector.OfType<PlanGrantRenewed>());
        Assert.Equal(grant.Id, published.GrantId);
        Assert.Equal(userId, published.UserId);
        Assert.Equal(plan.Id, published.PlanId);
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalFailed>());
    }

    [Fact]
    public async Task RenewalRefund_RollsBackPaidPeriod_DisablesAutoRenew_AndIsIdempotent()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-refund", failureCount: 0);
        DateTimeOffset previousExpiresAt = grant.ExpiresAt!.Value;

        Assert.Equal(1, await RunSweeperAsync());
        Order renewalOrder = await ExecuteInDbAsync(async db =>
            await db.Orders.AsNoTracking().SingleAsync(o => o.PlanId == plan.Id
                && o.ChargeType == OrderChargeType.RENEWAL));
        Assert.Equal(grant.Id, renewalOrder.RenewalGrantId);
        Assert.NotNull(renewalOrder.RenewalPreviousExpiresAt);
        Assert.Equal(
            previousExpiresAt,
            renewalOrder.RenewalPreviousExpiresAt.Value,
            TimeSpan.FromMicroseconds(1));
        Assert.True(renewalOrder.RenewalTargetExpiresAt > previousExpiresAt.AddDays(IntervalDays));

        UnitResult<Error> first = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrder.Id,
                renewalOrder.ExternalProviderRef!,
                "REFUNDED",
                "customer refund",
                RebillId: null));
        UnitResult<Error> replay = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrder.Id,
                renewalOrder.ExternalProviderRef!,
                "REFUNDED",
                "customer refund",
                RebillId: null));

        Assert.True(first.IsSuccess);
        Assert.True(replay.IsSuccess);
        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == renewalOrder.Id);
            PlanGrant rolledBack = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(OrderStatus.REFUNDED, order.Status);
            Assert.NotNull(rolledBack.ExpiresAt);
            Assert.Equal(previousExpiresAt, rolledBack.ExpiresAt.Value, TimeSpan.FromMicroseconds(1));
            Assert.Equal(PlanGrantStatus.EXPIRED, rolledBack.Status);
            Assert.Null(rolledBack.NextChargeAt);
        });
        PlanGrantRenewalRefunded refundEvent = Assert.Single(
            OutboxCollector.OfType<PlanGrantRenewalRefunded>());
        Assert.Equal(renewalOrder.Id, refundEvent.RenewalOrderId);
        Assert.Equal(
            previousExpiresAt,
            refundEvent.RolledBackExpiresAt,
            TimeSpan.FromMicroseconds(1));
    }

    [Fact]
    public async Task RenewalRefund_MissingWebhook_ReconciliationRollsBackPeriod()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-refund-reconcile", failureCount: 0);
        DateTimeOffset previousExpiresAt = grant.ExpiresAt!.Value;
        Assert.Equal(1, await RunSweeperAsync());

        Order renewalOrder = await ExecuteInDbAsync(async db =>
            await db.Orders.AsNoTracking().SingleAsync(o => o.PlanId == plan.Id
                && o.ChargeType == OrderChargeType.RENEWAL));
        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "REFUNDED",
            PaymentId = paymentId,
            OrderId = renewalOrder.Id.ToString(),
            Amount = renewalOrder.AmountCents,
            Message = "provider refund",
        };

        await Factory.Services.GetRequiredService<PendingOrderReconciliationService>()
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(renewalOrder.ExternalProviderRef, Assert.Single(Factory.TBankClient.GetStateCalls));
        await ExecuteInDbAsync(async db =>
        {
            Assert.Equal(
                OrderStatus.REFUNDED,
                (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == renewalOrder.Id)).Status);
            PlanGrant rolledBack = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.NotNull(rolledBack.ExpiresAt);
            Assert.Equal(previousExpiresAt, rolledBack.ExpiresAt.Value, TimeSpan.FromMicroseconds(1));
            Assert.Equal(PlanGrantStatus.EXPIRED, rolledBack.Status);
        });
        Assert.Single(OutboxCollector.OfType<PlanGrantRenewalRefunded>());
    }

    [Fact]
    public async Task RenewalRefund_PaidNotificationAlsoLost_ConvergesOrderWithoutGrantRollback()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-refund-before-paid", failureCount: 0);
        DateTimeOffset originalExpiresAt = grant.ExpiresAt!.Value;
        Order pendingRenewal = await SeedPendingRenewalOrderAsync(
            userId,
            plan.Id,
            "rebill-refund-before-paid",
            "payment-refunded-before-paid");

        UnitResult<Error> result = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                pendingRenewal.Id,
                "payment-refunded-before-paid",
                "REFUNDED",
                "provider refund",
                RebillId: null));

        Assert.True(result.IsSuccess);
        await ExecuteInDbAsync(async db =>
        {
            Assert.Equal(
                OrderStatus.REFUNDED,
                (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == pendingRenewal.Id)).Status);
            PlanGrant unchanged = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.NotNull(unchanged.ExpiresAt);
            Assert.Equal(originalExpiresAt, unchanged.ExpiresAt.Value, TimeSpan.FromMicroseconds(1));
            Assert.Equal(PlanGrantStatus.EXPIRED, unchanged.Status);
            Assert.Null(unchanged.NextChargeAt);
        });
        Assert.Single(OutboxCollector.OfType<PlanGrantRenewalRefunded>());

        Assert.Equal(0, await RunSweeperAsync());
        Assert.Empty(Factory.TBankClient.ChargeCalls);
    }

    [Fact]
    public async Task RenewalRefund_ConcurrentChargeLock_FailsBeforeMutation_ThenRetries()
    {
        await Factory.ResetDatabaseAsync();
        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-refund-lock", failureCount: 0);
        Assert.Equal(1, await RunSweeperAsync());
        Order renewalOrder = await ExecuteInDbAsync(async db =>
            await db.Orders.AsNoTracking().SingleAsync(o => o.PlanId == plan.Id
                && o.ChargeType == OrderChargeType.RENEWAL));

        await using AsyncServiceScope lockScope = Factory.Services.CreateAsyncScope();
        IOrdersRepository orders = lockScope.ServiceProvider.GetRequiredService<IOrdersRepository>();
        await using IAsyncDisposable heldLock = (await orders.TryAcquireRenewalLockAsync(
            grant.Id,
            CancellationToken.None))!;

        UnitResult<Error> blocked = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrder.Id,
                renewalOrder.ExternalProviderRef!,
                "REFUNDED",
                "concurrent refund",
                RebillId: null));

        Assert.True(blocked.IsFailure);
        Assert.Equal("payment.renewal.refund.lock_busy", blocked.Error.Messages[0].Code);
        await ExecuteInDbAsync(async db => Assert.Equal(
            OrderStatus.PAID,
            (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == renewalOrder.Id)).Status));

        await heldLock.DisposeAsync();
        UnitResult<Error> retried = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrder.Id,
                renewalOrder.ExternalProviderRef!,
                "REFUNDED",
                "concurrent refund",
                RebillId: null));
        Assert.True(retried.IsSuccess);
    }

    [Fact]
    public async Task RenewalRefund_LegacyPaidOrderWithoutSnapshot_ReconciliationDerivesPeriod()
    {
        await Factory.ResetDatabaseAsync();
        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        DateTimeOffset currentExpiry = DateTimeOffset.UtcNow.AddDays(60);
        PlanGrant grant = PlanGrant.Create(
            userId,
            plan.Id,
            PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(),
            expiresAt: currentExpiry,
            pricePaidCents: PriceCents);
        Assert.True(grant.AttachRecurring(
            "rebill-legacy-refund",
            userId.ToString(),
            SubscriptionRenewalPolicy.FirstChargeAt(currentExpiry)).IsSuccess);
        Order legacyOrder = Order.CreateRenewal(
            userId,
            plan.Id,
            grant.Id,
            PriceCents,
            "RUB",
            "rebill-legacy-refund",
            provider: "tbank").Value;
        Assert.True(legacyOrder.AttachExternalRef("payment-legacy-refund").IsSuccess);
        Assert.True(legacyOrder.MarkPaid("payment-legacy-refund").IsSuccess);
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            db.Orders.Add(legacyOrder);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE access.orders SET renewal_grant_id = NULL WHERE id = {legacyOrder.Id}");
        });
        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "REFUNDED",
            PaymentId = paymentId,
            OrderId = legacyOrder.Id.ToString(),
            Amount = legacyOrder.AmountCents,
        };

        await Factory.Services.GetRequiredService<PendingOrderReconciliationService>()
            .RunOnceAsync(CancellationToken.None);

        await ExecuteInDbAsync(async db =>
        {
            Order refunded = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == legacyOrder.Id);
            PlanGrant rolledBack = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(OrderStatus.REFUNDED, refunded.Status);
            Assert.Equal(grant.Id, refunded.RenewalGrantId);
            Assert.NotNull(rolledBack.ExpiresAt);
            Assert.Equal(
                currentExpiry.AddDays(-IntervalDays),
                rolledBack.ExpiresAt.Value,
                TimeSpan.FromMicroseconds(1));
            Assert.Null(rolledBack.NextChargeAt);
        });
    }

    [Fact]
    public async Task SweepOnce_ChargeFails_RecordsFailureAndSchedulesRetry_NoRenew()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId,
            plan.Id,
            rebillId: "rebill-fail",
            failureCount: 0,
            expiresAt: DateTimeOffset.UtcNow.AddHours(24));

        // Provider rejects the charge.
        Factory.TBankClient.ChargeHandler = _ => Error.Failure("tbank.charge.failed", "insufficient funds");

        int renewed = await RunSweeperAsync();
        Assert.Equal(0, renewed);

        // Charge error alone is ambiguous. The next tick records dunning only after
        // authoritative GetState reports a terminal provider status.
        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "REJECTED",
            PaymentId = paymentId,
        };
        renewed = await RunSweeperAsync();

        Assert.Equal(0, renewed);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant updated = await db.PlanGrants.AsNoTracking().FirstAsync(g => g.Id == grant.Id);
            Assert.Equal(PlanGrantStatus.ACTIVE, updated.Status); // not revoked
            Assert.Equal(1, updated.ChargeFailureCount);
            // First retry is anchored at the paid-through T boundary.
            Assert.NotNull(updated.NextChargeAt);
            Assert.Equal(updated.ExpiresAt, updated.NextChargeAt);
            Assert.Equal(updated.ExpiresAt!.Value.AddHours(72), updated.RenewalGraceEndsAt);

            Order renewalOrder = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.PlanId == plan.Id && o.ChargeType == OrderChargeType.RENEWAL);
            Assert.Equal(OrderStatus.FAILED, renewalOrder.Status);
        });

        PlanGrantRenewalFailed retry = Assert.Single(
            OutboxCollector.OfType<PlanGrantRenewalFailed>());
        Assert.Equal(SubscriptionLifecycleStages.RetryScheduled, retry.Stage);
        Assert.Equal(1, retry.Attempt);
        Assert.Equal(retry.ExpiresAt, retry.NextRetryAt);
        Assert.Equal(retry.ExpiresAt!.Value.AddHours(72), retry.GraceEndsAt);

        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewed>());
    }

    [Fact]
    public async Task RenewalFailedWebhook_RecordsCanonicalDunningAttempt_AndPublishesFailure()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        DateTimeOffset paidThrough = DateTimeOffset.UtcNow.AddHours(24);
        PlanGrant grant = await SeedDueGrantAsync(
            userId,
            plan.Id,
            rebillId: "rebill-webhook-failed",
            failureCount: 0,
            expiresAt: paidThrough);
        Order renewalOrder = await SeedPendingRenewalOrderAsync(
            userId,
            plan.Id,
            "rebill-webhook-failed",
            "payment-webhook-failed");

        UnitResult<Error> result = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrder.Id,
                "payment-webhook-failed",
                "FAILED",
                "insufficient funds",
                RebillId: null));

        Assert.True(result.IsSuccess);
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant updated = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Order failedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == renewalOrder.Id);
            Assert.Equal(OrderStatus.FAILED, failedOrder.Status);
            Assert.Equal(1, updated.ChargeFailureCount);
            Assert.Equal(updated.ExpiresAt, updated.NextChargeAt);
            Assert.Equal(
                SubscriptionRenewalPolicy.GraceEndsAt(updated.ExpiresAt!.Value),
                updated.RenewalGraceEndsAt);
        });

        PlanGrantRenewalFailed failed = Assert.Single(
            OutboxCollector.OfType<PlanGrantRenewalFailed>());
        Assert.Equal(grant.Id, failed.GrantId);
        Assert.Equal(1, failed.FailureCount);
        Assert.Equal(SubscriptionLifecycleStages.RetryScheduled, failed.Stage);
    }

    [Fact]
    public async Task RenewalFailedWebhook_AfterCancellation_ClosesOrderWithoutRestartingDunning()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId,
            plan.Id,
            rebillId: "rebill-failed-after-cancel",
            failureCount: 0,
            expiresAt: DateTimeOffset.UtcNow.AddDays(10));
        Order renewalOrder = await SeedPendingRenewalOrderAsync(
            userId,
            plan.Id,
            "rebill-failed-after-cancel",
            "payment-failed-after-cancel");
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant tracked = await db.PlanGrants.SingleAsync(g => g.Id == grant.Id);
            Assert.True(tracked.CancelAutoRenewal(DateTimeOffset.UtcNow).IsSuccess);
            await db.SaveChangesAsync();
        });

        UnitResult<Error> result = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrder.Id,
                "payment-failed-after-cancel",
                "FAILED",
                "late provider failure",
                RebillId: null));

        Assert.True(result.IsSuccess);
        await ExecuteInDbAsync(async db =>
        {
            Assert.Equal(
                OrderStatus.FAILED,
                (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == renewalOrder.Id)).Status);
            PlanGrant unchanged = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.NotNull(unchanged.AutoRenewalCancelledAt);
            Assert.Equal(0, unchanged.ChargeFailureCount);
            Assert.Null(unchanged.RenewalGraceEndsAt);
            Assert.Null(unchanged.NextChargeAt);
        });
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalFailed>());
    }

    [Fact]
    public async Task RenewalFailedWebhook_AfterHardGraceExpiry_ClosesOrderWithoutRestartingDunning()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        DateTimeOffset paidThrough = DateTimeOffset.UtcNow.AddHours(-73);
        PlanGrant grant = await SeedDueGrantAsync(
            userId,
            plan.Id,
            rebillId: "rebill-failed-after-expiry",
            failureCount: 0,
            nextChargeAt: paidThrough.AddHours(-24),
            expiresAt: paidThrough);
        Order renewalOrder = await SeedPendingRenewalOrderAsync(
            userId,
            plan.Id,
            "rebill-failed-after-expiry",
            "payment-failed-after-expiry");
        Assert.Equal(1, await RunExpiredGrantsSweeperAsync());

        UnitResult<Error> result = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrder.Id,
                "payment-failed-after-expiry",
                "FAILED",
                "late provider failure",
                RebillId: null));

        Assert.True(result.IsSuccess);
        await ExecuteInDbAsync(async db =>
        {
            Assert.Equal(
                OrderStatus.FAILED,
                (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == renewalOrder.Id)).Status);
            PlanGrant unchanged = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(PlanGrantStatus.EXPIRED, unchanged.Status);
            Assert.Equal(0, unchanged.ChargeFailureCount);
            Assert.Null(unchanged.RenewalGraceEndsAt);
        });
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewalFailed>());
    }

    [Fact]
    public async Task SweepOnce_TwoConcurrentWorkers_ChargesDueGrantOnlyOnce()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        await SeedDueGrantAsync(userId, plan.Id, rebillId: "rebill-concurrent", failureCount: 0);

        using var firstChargeEntered = new ManualResetEventSlim();
        using var releaseFirstCharge = new ManualResetEventSlim();
        int chargeAttempts = 0;
        Factory.TBankClient.ChargeHandler = args =>
        {
            int attempt = Interlocked.Increment(ref chargeAttempts);
            if (attempt == 1)
            {
                firstChargeEntered.Set();
                Assert.True(releaseFirstCharge.Wait(TimeSpan.FromSeconds(10)));
            }

            return new TBankChargeResponse
            {
                Success = true,
                Status = "CONFIRMED",
                PaymentId = args.PaymentId,
            };
        };

        Task<int> first = Task.Run(() => RunSweeperAsync());
        Assert.True(firstChargeEntered.Wait(TimeSpan.FromSeconds(10)));
        Task<int> second = Task.Run(() => RunSweeperAsync());
        await Task.Delay(500);
        releaseFirstCharge.Set();

        await Task.WhenAll(first, second);

        Assert.Equal(1, chargeAttempts);
    }

    [Fact]
    public async Task RenewalWebhook_AfterWorkerCrash_RenewsGrantFromDurablePendingOrder()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-recovery", failureCount: 0);

        Order renewalOrder = Order.CreateRenewal(
            userId,
            plan.Id,
            grant.Id,
            PriceCents,
            "RUB",
            "rebill-recovery",
            provider: "tbank").Value;
        Assert.True(renewalOrder.AttachExternalRef("payment-recovery").IsSuccess);
        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(renewalOrder);
            await db.SaveChangesAsync();
        });

        DateTimeOffset before = DateTimeOffset.UtcNow;
        UnitResult<Error> result = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrder.Id,
                "payment-recovery",
                "PAID",
                null,
                RebillId: "rebill-recovery"));
        DateTimeOffset after = DateTimeOffset.UtcNow;

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.GetMessage() : null);
        await ExecuteInDbAsync(async db =>
        {
            Order updatedOrder = await db.Orders.AsNoTracking()
                .SingleAsync(o => o.Id == renewalOrder.Id);
            PlanGrant updatedGrant = await db.PlanGrants.AsNoTracking()
                .SingleAsync(g => g.Id == grant.Id);

            Assert.Equal(OrderStatus.PAID, updatedOrder.Status);
            Assert.InRange(
                updatedGrant.ExpiresAt!.Value,
                before.AddDays(IntervalDays).AddSeconds(-5),
                after.AddDays(IntervalDays).AddSeconds(5));
            Assert.Equal(
                SubscriptionRenewalPolicy.FirstChargeAt(updatedGrant.ExpiresAt.Value),
                updatedGrant.NextChargeAt);
        });

        PlanGrantRenewed renewed = Assert.Single(OutboxCollector.OfType<PlanGrantRenewed>());
        Assert.Equal(grant.Id, renewed.GrantId);
    }

    [Fact]
    public async Task RenewalWebhook_AfterExpiryRace_ReactivatesGrant()
    {
        await Factory.ResetDatabaseAsync();
        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-expiry-race", failureCount: 0);
        Order renewalOrder = await SeedPendingRenewalOrderAsync(
            userId, plan.Id, "rebill-expiry-race", "payment-expiry-race");
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant expiring = await db.PlanGrants.SingleAsync(g => g.Id == grant.Id);
            Assert.True(expiring.Expire().IsSuccess);
            await db.SaveChangesAsync();
        });

        UnitResult<Error> result = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrder.Id,
                "payment-expiry-race",
                "PAID",
                null,
                RebillId: "rebill-expiry-race"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.GetMessage() : null);
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant renewed = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(PlanGrantStatus.ACTIVE, renewed.Status);
            Assert.True(renewed.ExpiresAt > DateTimeOffset.UtcNow.AddDays(IntervalDays - 1));
        });
    }

    [Fact]
    public async Task Concurrent_stale_expiry_cannot_overwrite_confirmed_renewal()
    {
        await Factory.ResetDatabaseAsync();
        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-xmin", failureCount: 0);

        await using AsyncServiceScope renewalScope = Factory.Services.CreateAsyncScope();
        await using AsyncServiceScope expiryScope = Factory.Services.CreateAsyncScope();
        AccessServiceDbContext renewalDb =
            renewalScope.ServiceProvider.GetRequiredService<AccessServiceDbContext>();
        AccessServiceDbContext expiryDb =
            expiryScope.ServiceProvider.GetRequiredService<AccessServiceDbContext>();
        PlanGrant renewalCopy = await renewalDb.PlanGrants.SingleAsync(g => g.Id == grant.Id);
        PlanGrant staleExpiryCopy = await expiryDb.PlanGrants.SingleAsync(g => g.Id == grant.Id);

        DateTimeOffset renewedUntil = DateTimeOffset.UtcNow.AddDays(IntervalDays);
        Assert.True(renewalCopy.Renew(
            renewedUntil,
            SubscriptionRenewalPolicy.FirstChargeAt(renewedUntil)).IsSuccess);
        Assert.True(staleExpiryCopy.Expire().IsSuccess);
        await renewalDb.SaveChangesAsync();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => expiryDb.SaveChangesAsync());
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant persisted = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id);
            Assert.Equal(PlanGrantStatus.ACTIVE, persisted.Status);
            Assert.NotNull(persisted.ExpiresAt);
            Assert.Equal(renewedUntil, persisted.ExpiresAt.Value, TimeSpan.FromMicroseconds(1));
        });
    }

    [Fact]
    public async Task SweepOnce_ResumesPendingOrder_GetStateConfirmed_DoesNotChargeAgain()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-state-confirmed", failureCount: 0);
        Order order = await SeedPendingRenewalOrderAsync(
            userId, plan.Id, "rebill-state-confirmed", "payment-state-confirmed");
        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = paymentId,
        };

        int renewed = await RunSweeperAsync();

        Assert.Equal(1, renewed);
        Assert.Equal("payment-state-confirmed", Assert.Single(Factory.TBankClient.GetStateCalls));
        Assert.Empty(Factory.TBankClient.InitCalls);
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        await ExecuteInDbAsync(async db =>
        {
            Assert.Equal(
                OrderStatus.PAID,
                (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status);
            Assert.True(
                (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id)).ExpiresAt
                > DateTimeOffset.UtcNow.AddDays(IntervalDays - 1));
        });
    }

    [Fact]
    public async Task SweepOnce_ResumesPendingOrder_GetStateNew_ChargesExactlyOnce()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        await SeedDueGrantAsync(userId, plan.Id, rebillId: "rebill-state-new", failureCount: 0);
        await SeedPendingRenewalOrderAsync(
            userId, plan.Id, "rebill-state-new", "payment-state-new");
        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "NEW",
            PaymentId = paymentId,
        };

        int renewed = await RunSweeperAsync();

        Assert.Equal(1, renewed);
        Assert.Equal("payment-state-new", Assert.Single(Factory.TBankClient.GetStateCalls));
        Assert.Empty(Factory.TBankClient.InitCalls);
        Assert.Equal("payment-state-new", Assert.Single(Factory.TBankClient.ChargeCalls).PaymentId);
    }

    [Fact]
    public async Task SweepOnce_AmbiguousChargeRecovery_GetStateNew_DoesNotChargeTwice()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        await SeedDueGrantAsync(userId, plan.Id, rebillId: "rebill-charge-window", failureCount: 0);
        Factory.TBankClient.ChargeHandler = _ => TBankErrors.NetworkError("timeout");

        Assert.Equal(0, await RunSweeperAsync());
        Assert.Single(Factory.TBankClient.ChargeCalls);

        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "NEW",
            PaymentId = paymentId,
        };

        Assert.Equal(0, await RunSweeperAsync());
        Assert.Single(Factory.TBankClient.ChargeCalls);
    }

    [Fact]
    public async Task SweepOnce_AmbiguousRenewalInit_CheckOrderRecoversWithoutSecondInit()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-renewal-init-window", failureCount: 0);
        Factory.TBankClient.InitHandler = _ => TBankErrors.NetworkError("timeout");

        Assert.Equal(0, await RunSweeperAsync());
        TBankInitRequest firstInit = Assert.Single(Factory.TBankClient.InitCalls);

        Factory.TBankClient.CheckOrderHandler = orderId => new TBankCheckOrderResponse
        {
            Success = true,
            OrderId = orderId,
            Payments =
            [
                new TBankPaymentHistory
                {
                    PaymentId = "payment-renewal-init-window",
                    Amount = firstInit.Amount,
                    Status = "CONFIRMED",
                    Success = true,
                },
            ],
        };

        Assert.Equal(1, await RunSweeperAsync());
        Assert.Single(Factory.TBankClient.InitCalls);
        Assert.Single(Factory.TBankClient.CheckOrderCalls);
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        await ExecuteInDbAsync(async db =>
        {
            Assert.Equal(
                OrderStatus.PAID,
                (await db.Orders.AsNoTracking().SingleAsync(o => o.PlanId == plan.Id
                    && o.ChargeType == OrderChargeType.RENEWAL)).Status);
            Assert.Equal(
                0,
                (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id)).ChargeFailureCount);
        });
    }

    [Fact]
    public async Task SweepOnce_AmbiguousInitRecovery_CheckOrderFindsConfirmedPayment_DoesNotInitOrChargeAgain()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        PlanGrant grant = await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-init-window", failureCount: 0);
        Order order = await SeedPendingRenewalOrderAsync(
            userId, plan.Id, "rebill-init-window", paymentId: null);

        // The previous process called Init successfully and then crashed before persisting
        // PaymentId. Keep that single provider call in the fake's history.
        await Factory.TBankClient.InitAsync(new TBankInitRequest
        {
            OrderId = order.Id.ToString(),
            Amount = order.AmountCents,
        });
        Factory.TBankClient.CheckOrderHandler = orderId => new TBankCheckOrderResponse
        {
            Success = true,
            OrderId = orderId,
            Payments =
            [
                new TBankPaymentHistory
                {
                    PaymentId = "payment-init-window",
                    Amount = order.AmountCents,
                    Status = "CONFIRMED",
                    Success = true,
                },
            ],
        };

        int renewed = await RunSweeperAsync();

        Assert.Equal(1, renewed);
        Assert.Single(Factory.TBankClient.InitCalls);
        Assert.Equal(order.Id.ToString(), Assert.Single(Factory.TBankClient.CheckOrderCalls));
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        await ExecuteInDbAsync(async db =>
        {
            Order persisted = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, persisted.Status);
            Assert.Equal("payment-init-window", persisted.ExternalProviderRef);
            Assert.True(
                (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id)).ExpiresAt
                > DateTimeOffset.UtcNow.AddDays(IntervalDays - 1));
        });
    }

    [Fact]
    public async Task SweepOnce_FailureReachesPolicyLimit_PublishesRenewalFailed_LeavesGrantInGrace()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        // Already failed at T-24h and T; the due T+48h attempt is terminal.
        DateTimeOffset thirdAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        PlanGrant grant = await SeedDueGrantAsync(
            userId,
            plan.Id,
            rebillId: "rebill-terminal",
            failureCount: 2,
            nextChargeAt: thirdAttemptAt,
            expiresAt: thirdAttemptAt.AddHours(-48));

        Factory.TBankClient.ChargeHandler = _ => Error.Failure("tbank.charge.failed", "card expired");

        int renewed = await RunSweeperAsync();
        Assert.Equal(0, renewed);

        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "REJECTED",
            PaymentId = paymentId,
        };
        renewed = await RunSweeperAsync();

        Assert.Equal(0, renewed);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant updated = await db.PlanGrants.AsNoTracking().FirstAsync(g => g.Id == grant.Id);
            // NOT force-revoked — grant stays ACTIVE and runs out at its current ExpiresAt.
            Assert.Equal(PlanGrantStatus.ACTIVE, updated.Status);
            Assert.Equal(3, updated.ChargeFailureCount);
        });

        PlanGrantRenewalFailed failed = Assert.Single(OutboxCollector.OfType<PlanGrantRenewalFailed>());
        Assert.Equal(grant.Id, failed.GrantId);
        Assert.Equal(3, failed.FailureCount);
        Assert.Equal(plan.Id, failed.PlanId);
        Assert.Equal(SubscriptionLifecycleStages.TerminalFailure, failed.Stage);
        Assert.Null(failed.NextRetryAt);
        Assert.Equal(failed.ExpiresAt!.Value.AddHours(72), failed.GraceEndsAt);
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewed>());
    }

    [Fact]
    public async Task ExpirySweep_PaidPeriodElapsedButGraceActive_KeepsAccessActive()
    {
        await Factory.ResetDatabaseAsync();

        Plan plan = await CreateRecurringPlanAsync();
        DateTimeOffset paidThrough = DateTimeOffset.UtcNow.AddHours(-1);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(),
            plan.Id,
            PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(),
            expiresAt: paidThrough,
            pricePaidCents: PriceCents);
        Assert.True(grant.AttachRecurring("rebill-grace-active", grant.UserId.ToString(), paidThrough).IsSuccess);
        Assert.True(grant.RecordChargeFailure(
            nextRetryAt: null,
            renewalGraceEndsAt: SubscriptionRenewalPolicy.GraceEndsAt(paidThrough)).IsSuccess);
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        Assert.Equal(0, await RunExpiredGrantsSweeperAsync());

        await ExecuteInDbAsync(async db =>
            Assert.Equal(
                PlanGrantStatus.ACTIVE,
                (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id)).Status));
    }

    [Fact]
    public async Task ExpirySweep_FirstRenewalAttemptMissed_KeepsRecurringAccessThroughHardGraceBoundary()
    {
        await Factory.ResetDatabaseAsync();

        Plan plan = await CreateRecurringPlanAsync();
        DateTimeOffset paidThrough = DateTimeOffset.UtcNow.AddHours(-1);
        PlanGrant grant = await SeedDueGrantAsync(
            Guid.NewGuid(),
            plan.Id,
            rebillId: "rebill-missed-first-attempt",
            failureCount: 0,
            nextChargeAt: paidThrough.AddHours(-24),
            expiresAt: paidThrough);

        Assert.Equal(0, await RunExpiredGrantsSweeperAsync());

        await ExecuteInDbAsync(async db => Assert.Equal(
            PlanGrantStatus.ACTIVE,
            (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id)).Status));
    }

    [Fact]
    public async Task SweepOnce_FirstAttemptAfterHardGraceBoundary_DoesNotCallProvider()
    {
        await Factory.ResetDatabaseAsync();

        Plan plan = await CreateRecurringPlanAsync();
        DateTimeOffset paidThrough = DateTimeOffset.UtcNow
            .Subtract(SubscriptionRenewalPolicy.GracePeriod)
            .AddMinutes(-1);
        await SeedDueGrantAsync(
            Guid.NewGuid(),
            plan.Id,
            rebillId: "rebill-after-hard-grace",
            failureCount: 0,
            nextChargeAt: paidThrough.AddHours(-24),
            expiresAt: paidThrough);

        Assert.Equal(0, await RunSweeperAsync());
        Assert.Empty(Factory.TBankClient.InitCalls);
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewed>());
    }

    [Fact]
    public async Task ExpirySweep_GraceElapsed_ExpiresAccess()
    {
        await Factory.ResetDatabaseAsync();

        Plan plan = await CreateRecurringPlanAsync();
        DateTimeOffset paidThrough = DateTimeOffset.UtcNow.AddHours(-73);
        PlanGrant grant = PlanGrant.Create(
            Guid.NewGuid(),
            plan.Id,
            PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(),
            expiresAt: paidThrough,
            pricePaidCents: PriceCents);
        Assert.True(grant.AttachRecurring("rebill-grace-ended", grant.UserId.ToString(), paidThrough).IsSuccess);
        Assert.True(grant.RecordChargeFailure(
            nextRetryAt: null,
            renewalGraceEndsAt: SubscriptionRenewalPolicy.GraceEndsAt(paidThrough)).IsSuccess);
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        Assert.Equal(1, await RunExpiredGrantsSweeperAsync());

        await ExecuteInDbAsync(async db =>
            Assert.Equal(
                PlanGrantStatus.EXPIRED,
                (await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grant.Id)).Status));
    }

    [Fact]
    public async Task SweepOnce_NotDueGrant_Skipped()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();
        // NextChargeAt in the future → not due.
        await SeedDueGrantAsync(
            userId, plan.Id, rebillId: "rebill-future", failureCount: 0,
            nextChargeAt: DateTimeOffset.UtcNow.AddDays(10));

        int renewed = await RunSweeperAsync();

        Assert.Equal(0, renewed);
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewed>());
    }

    [Fact]
    public async Task SweepOnce_NonRecurringGrantWithStrayNextChargeAt_Skipped()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        // Lifetime FULL_ALL plan (Term.Kind != RECURRING) — must never be charged.
        Plan plan = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.FULL_ALL,
            slug: PlanSlug.Of($"life-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Полный доступ").Value,
            courseIds: [],
            requestedCapabilities: null).Value;
        plan.UpdatePrice(PriceCents, "RUB");
        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });

        await SeedDueGrantAsync(userId, plan.Id, rebillId: "rebill-stray", failureCount: 0);

        int renewed = await RunSweeperAsync();

        Assert.Equal(0, renewed);
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        Assert.Empty(OutboxCollector.OfType<PlanGrantRenewed>());
    }

    [Fact]
    public async Task SweepOnce_InvalidPlanDoesNotStarveBoundedBatch()
    {
        await Factory.ResetDatabaseAsync();

        Plan invalidPlan = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.FULL_ALL,
            slug: PlanSlug.Of($"invalid-renewal-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Invalid renewal plan").Value,
            courseIds: [],
            requestedCapabilities: null).Value;
        invalidPlan.UpdatePrice(PriceCents, "RUB");
        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(invalidPlan);
            await db.SaveChangesAsync();
        });

        Plan validPlan = await CreateRecurringPlanAsync();
        await SeedDueGrantAsync(
            Guid.NewGuid(),
            invalidPlan.Id,
            "rebill-invalid-head",
            failureCount: 0,
            nextChargeAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        await SeedDueGrantAsync(
            Guid.NewGuid(),
            validPlan.Id,
            "rebill-valid-behind",
            failureCount: 0,
            nextChargeAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(1, await RunSweeperAsync(batchSize: 1));
        Assert.Single(Factory.TBankClient.ChargeCalls);
        Assert.Single(OutboxCollector.OfType<PlanGrantRenewed>());
    }

    [Fact]
    public async Task RenewalWebhook_IsIdempotent_DoesNotDoubleGrant()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateRecurringPlanAsync();

        // The sweeper renews the grant on the sync Charge response. Then the renewal Charge's
        // NotificationURL backup webhook arrives for the same RENEWAL order — it must NOT
        // create a second grant.
        await SeedDueGrantAsync(userId, plan.Id, rebillId: "rebill-webhook", failureCount: 0);
        int renewed = await RunSweeperAsync();
        Assert.Equal(1, renewed);

        Guid renewalOrderId = await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.AsNoTracking()
                .FirstAsync(o => o.PlanId == plan.Id && o.ChargeType == OrderChargeType.RENEWAL);
            return order.Id;
        });
        string paymentId = Factory.TBankClient.ChargeCalls[0].PaymentId;

        // Fire the backup webhook for the RENEWAL order.
        UnitResult<Error> result = await HandleWebhookAndSaveAsync(
            new Contracts.Billing.PaymentWebhookRequest(
                renewalOrderId,
                paymentId,
                "PAID",
                null,
                RebillId: "rebill-webhook"));
        Assert.True(result.IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            // Exactly one grant for this (user, plan) — the webhook did NOT double-grant.
            int grantCount = await db.PlanGrants.AsNoTracking()
                .CountAsync(g => g.UserId == userId && g.PlanId == plan.Id);
            Assert.Equal(1, grantCount);
        });
    }

    private async Task<UnitResult<Error>> HandleWebhookAndSaveAsync(
        Contracts.Billing.PaymentWebhookRequest request)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        Core.Features.Billing.UseCases.PaymentWebhookHandler handler =
            scope.ServiceProvider.GetRequiredService<Core.Features.Billing.UseCases.PaymentWebhookHandler>();
        ITransactionManager transactions =
            scope.ServiceProvider.GetRequiredService<ITransactionManager>();

        UnitResult<Error> result = await handler.Handle(request, CancellationToken.None);
        if (result.IsFailure)
        {
            return result;
        }

        return await transactions.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<int> RunSweeperAsync(int? batchSize = null)
    {
        RecurringChargesSweeperOptions baseline =
            Factory.Services.GetRequiredService<IOptionsMonitor<RecurringChargesSweeperOptions>>().CurrentValue;

        RecurringChargesSweeperOptions opts = new()
        {
            SweepInterval = baseline.SweepInterval,
            BatchSize = batchSize ?? baseline.BatchSize,
            Enabled = baseline.Enabled,
        };

        ILogger<RecurringChargesSweeper> logger =
            Factory.Services.GetRequiredService<ILogger<RecurringChargesSweeper>>();

        RecurringChargesSweeper sweeper = new(Factory.Services, new StaticOptionsMonitor(opts), logger);
        return await sweeper.SweepOnceAsync(CancellationToken.None);
    }

    private async Task<int> RunExpiredGrantsSweeperAsync()
    {
        IOptionsMonitor<ExpiredGrantsSweeperOptions> options =
            Factory.Services.GetRequiredService<IOptionsMonitor<ExpiredGrantsSweeperOptions>>();
        ILogger<ExpiredGrantsSweeper> logger =
            Factory.Services.GetRequiredService<ILogger<ExpiredGrantsSweeper>>();
        ExpiredGrantsSweeper sweeper = new(Factory.Services, options, logger);
        return await sweeper.SweepOnceAsync(CancellationToken.None);
    }

    private async Task<Plan> CreateRecurringPlanAsync()
    {
        Plan plan = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.SUBSCRIPTION,
            slug: PlanSlug.Of($"sub-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Trainer Pro").Value,
            courseIds: [],
            requestedCapabilities: null,
            offerType: null,
            trialDurationDays: null,
            term: PlanTerm.Recurring(IntervalDays)).Value;
        plan.UpdatePrice(PriceCents, "RUB");

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });

        return plan;
    }

    /// <summary>
    /// Seeds an ACTIVE PURCHASE grant in subscription mode (RebillId attached, due NextChargeAt).
    /// </summary>
    private async Task<PlanGrant> SeedDueGrantAsync(
        Guid userId,
        Guid planId,
        string rebillId,
        int failureCount,
        DateTimeOffset? nextChargeAt = null,
        DateTimeOffset? expiresAt = null)
    {
        DateTimeOffset due = nextChargeAt ?? DateTimeOffset.UtcNow.AddMinutes(-1);

        PlanGrant grant = PlanGrant.Create(
            userId,
            planId,
            PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(),
            expiresAt: expiresAt ?? due,
            pricePaidCents: PriceCents);

        UnitResult<Error> attach = grant.AttachRecurring(rebillId, userId.ToString(), due);
        Assert.True(attach.IsSuccess);

        for (int i = 0; i < failureCount; i++)
        {
            DateTimeOffset paidThrough = grant.ExpiresAt!.Value;
            int completedFailureCount = grant.ChargeFailureCount + 1;
            UnitResult<Error> rec = grant.RecordChargeFailure(
                SubscriptionRenewalPolicy.NextRetryAt(paidThrough, completedFailureCount),
                SubscriptionRenewalPolicy.GraceEndsAt(paidThrough));
            Assert.True(rec.IsSuccess);
        }

        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });

        return grant;
    }

    private async Task<Order> SeedPendingRenewalOrderAsync(
        Guid userId,
        Guid planId,
        string rebillId,
        string? paymentId)
    {
        Guid grantId = await ExecuteInDbAsync(async db =>
            await db.PlanGrants.AsNoTracking()
                .Where(g => g.UserId == userId
                    && g.PlanId == planId
                    && g.Status == PlanGrantStatus.ACTIVE)
                .Select(g => g.Id)
                .SingleAsync());
        Order order = Order.CreateRenewal(
            userId,
            planId,
            grantId,
            PriceCents,
            "RUB",
            rebillId,
            provider: "tbank").Value;

        if (paymentId is not null)
        {
            Assert.True(order.AttachExternalRef(paymentId).IsSuccess);
        }

        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });

        return order;
    }

    /// <summary>Minimal <see cref="IOptionsMonitor{T}"/> wrapping a fixed options instance.</summary>
    private sealed class StaticOptionsMonitor : IOptionsMonitor<RecurringChargesSweeperOptions>
    {
        public StaticOptionsMonitor(RecurringChargesSweeperOptions value) => CurrentValue = value;

        public RecurringChargesSweeperOptions CurrentValue { get; }

        public RecurringChargesSweeperOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<RecurringChargesSweeperOptions, string?> listener) => null;
    }
}
