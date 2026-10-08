using AccessService.Core.Features.Billing.Reconciliation;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

/// <summary>
/// Integration tests for <see cref="PendingOrderReconciliationService"/> (Phase F.1.4, issue #102).
///
/// Reconciliation polls T-Bank GetState for PENDING orders &gt; <c>MinAgeSecondsBeforePoll</c>
/// old и применяет результат через тот же idempotent path что и webhook. Это
/// safety-net против потерянных webhook'ов: один потерянный webhook = один
/// потерянный grant = жалоба клиента.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class PendingOrderReconciliationTests : AccessServiceTestsBase
{
    private const int DEFAULT_AMOUNT_CENTS = 500_000;
    private const string DEFAULT_PAYMENT_ID = "9999999999";

    public PendingOrderReconciliationTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task RunOnceAsync_PendingConfirmed_MarksOrderPaidAndCreatesGrant()
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5);

        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
            Amount = order.AmountCents,
        };

        PendingOrderReconciliationService svc =
            Services.GetRequiredService<PendingOrderReconciliationService>();
        await svc.RunOnceAsync(CancellationToken.None);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, updated.Status);

            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g =>
                g.UserId == order.UserId
                && g.PlanId == order.PlanId
                && g.SourceRef == order.Id
                && g.Status == PlanGrantStatus.ACTIVE);
            Assert.Equal(1, grantCount);

            // RECONCILIATION_RECOVERED audit row recorded.
            int reconciliationEvents = await db.OrderEvents.AsNoTracking().CountAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.RECONCILIATION_RECOVERED);
            Assert.Equal(1, reconciliationEvents);
        });
    }

    [Fact]
    public async Task RunOnceAsync_PendingRejected_MarksFailed_NoGrant()
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5);

        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "REJECTED",
            ErrorCode = "1051",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
            Amount = order.AmountCents,
        };

        PendingOrderReconciliationService svc =
            Services.GetRequiredService<PendingOrderReconciliationService>();
        await svc.RunOnceAsync(CancellationToken.None);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.FAILED, updated.Status);

            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g => g.SourceRef == order.Id);
            Assert.Equal(0, grantCount);
        });
    }

    [Fact]
    public async Task RunOnceAsync_TooYoung_NotPolled()
    {
        // ageMinutes=0 → CreatedAt=now → younger than MinAgeSecondsBeforePoll=120.
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 0);

        bool wasCalled = false;
        Factory.TBankClient.GetStateHandler = paymentId =>
        {
            wasCalled = true;
            return new TBankGetStateResponse { Success = true, Status = "CONFIRMED", PaymentId = paymentId };
        };

        PendingOrderReconciliationService svc =
            Services.GetRequiredService<PendingOrderReconciliationService>();
        await svc.RunOnceAsync(CancellationToken.None);

        Assert.False(wasCalled);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PENDING, updated.Status);
        });
    }

    [Fact]
    public async Task RunOnceAsync_OrderOlderThan24hButConfirmed_RecoversBeforeExpiring()
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 25 * 60);

        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
            Amount = order.AmountCents,
        };

        PendingOrderReconciliationService svc =
            Services.GetRequiredService<PendingOrderReconciliationService>();
        await svc.RunOnceAsync(CancellationToken.None);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, updated.Status);

            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g =>
                g.SourceRef == order.Id && g.Status == PlanGrantStatus.ACTIVE);
            Assert.Equal(1, grantCount);
        });
    }

    [Fact]
    public async Task RunOnceAsync_OrderOlderThan24h_MarksExpired()
    {
        // 25h > MaxPendingHoursBeforeExpire=24, but provider is polled once before expiry.
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 25 * 60);

        bool wasCalled = false;
        Factory.TBankClient.GetStateHandler = paymentId =>
        {
            wasCalled = true;
            return new TBankGetStateResponse { Success = true, Status = "NEW", PaymentId = paymentId };
        };

        PendingOrderReconciliationService svc =
            Services.GetRequiredService<PendingOrderReconciliationService>();
        await svc.RunOnceAsync(CancellationToken.None);

        Assert.True(wasCalled);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.FAILED, updated.Status);
            Assert.Contains("expired", updated.FailureReason ?? string.Empty, StringComparison.Ordinal);

            int markFailedEvents = await db.OrderEvents.AsNoTracking().CountAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.MARK_FAILED);
            Assert.Equal(1, markFailedEvents);
        });
    }

    [Fact]
    public async Task RunOnceAsync_IntermediateStatus_LeavesOrderPending()
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5);

        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "AUTHORIZING",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
        };

        PendingOrderReconciliationService svc =
            Services.GetRequiredService<PendingOrderReconciliationService>();
        await svc.RunOnceAsync(CancellationToken.None);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PENDING, updated.Status);

            int grantCount = await db.PlanGrants.AsNoTracking().CountAsync(g => g.SourceRef == order.Id);
            Assert.Equal(0, grantCount);

            int reconciliationEvents = await db.OrderEvents.AsNoTracking().CountAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.RECONCILIATION_RECOVERED);
            Assert.Equal(0, reconciliationEvents);
        });
    }

    [Fact]
    public async Task RunOnceAsync_AuthorizedState_DoesNotCreateRepeatedTerminalAudit()
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5, subscription: true);
        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "AUTHORIZED",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
        };

        PendingOrderReconciliationService svc =
            Services.GetRequiredService<PendingOrderReconciliationService>();
        await svc.RunOnceAsync(CancellationToken.None);

        await ExecuteInDbAsync(async db =>
        {
            Order updated = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PENDING, updated.Status);
            Assert.Null(updated.RebillId);
            Assert.Equal(0, await db.OrderEvents.CountAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.RECONCILIATION_RECOVERED));
        });
    }

    [Fact]
    public async Task RunOnceAsync_RefundedProviderState_ConvergesPendingOrderToRefunded()
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5);
        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "REFUNDED",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
        };

        PendingOrderReconciliationService svc =
            Services.GetRequiredService<PendingOrderReconciliationService>();
        await svc.RunOnceAsync(CancellationToken.None);

        await ExecuteInDbAsync(async db => Assert.Equal(
            OrderStatus.REFUNDED,
            (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status));
    }

    [Fact]
    public async Task RunOnceAsync_MissingWebhookRecovery_CheckOrderCreatesOneGrantAndOneEvent()
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5, attachExternalRef: false);
        Factory.TBankClient.CheckOrderHandler = orderId => SuccessfulCheckOrder(
            orderId,
            order.AmountCents,
            "payment-missing-webhook",
            "CONFIRMED");

        PendingOrderReconciliationService service =
            Services.GetRequiredService<PendingOrderReconciliationService>();
        await service.RunOnceAsync(CancellationToken.None);
        await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(order.Id.ToString(), Assert.Single(Factory.TBankClient.CheckOrderCalls));
        Assert.Empty(Factory.TBankClient.InitCalls);
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        Assert.Single(OutboxCollector.OfType<Shared.Messaging.IntegrationEvents.Access.Events.PlanGrantCreated>());
        await ExecuteInDbAsync(async db =>
        {
            Order persisted = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, persisted.Status);
            Assert.Equal("payment-missing-webhook", persisted.ExternalProviderRef);
            Assert.Equal(1, await db.PlanGrants.CountAsync(g => g.SourceRef == order.Id));
            Assert.Equal(1, await db.OrderEvents.CountAsync(e =>
                e.OrderId == order.Id && e.EventType == OrderEventType.RECONCILIATION_RECOVERED));
        });
    }

    [Fact]
    public async Task RunOnceAsync_RebillRecovery_UniqueActiveCardAttachesRecurringToken()
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5, subscription: true);
        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
            Amount = order.AmountCents,
        };
        Factory.TBankClient.GetCardListHandler = _ => new TBankCard[]
        {
            new() { CardId = "deleted", Status = "D", RebillId = "old" },
            new() { CardId = "active", Status = "A", RebillId = "rebill-recovered" },
        };

        await Services.GetRequiredService<PendingOrderReconciliationService>()
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(order.UserId.ToString(), Assert.Single(Factory.TBankClient.GetCardListCalls));
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        await ExecuteInDbAsync(async db =>
        {
            Order persisted = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.SourceRef == order.Id);
            Assert.Equal("rebill-recovered", persisted.RebillId);
            Assert.Equal("rebill-recovered", grant.RebillId);
            Assert.NotNull(grant.NextChargeAt);
        });
    }

    [Fact]
    public async Task RunOnceAsync_RebillRecovery_LostAuthorizedAfterPaid_AttachesTokenToExistingGrant()
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5, subscription: true);
        await using (AsyncServiceScope scope = Factory.Services.CreateAsyncScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<
                Core.Features.Billing.UseCases.PaymentWebhookHandler>();
            var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
            UnitResult<Error> handled = await handler.Handle(
                new Contracts.Billing.PaymentWebhookRequest(
                    order.Id,
                    order.ExternalProviderRef!,
                    "PAID",
                    null,
                    RebillId: null),
                CancellationToken.None);
            Assert.True(handled.IsSuccess);
            Assert.True((await transactions.SaveChangesAsync(CancellationToken.None)).IsSuccess);
        }

        Factory.TBankClient.GetCardListHandler = _ => new TBankCard[]
        {
            new() { CardId = "active", Status = "A", RebillId = "rebill-after-paid" },
        };

        await Services.GetRequiredService<PendingOrderReconciliationService>()
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(order.UserId.ToString(), Assert.Single(Factory.TBankClient.GetCardListCalls));
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        await ExecuteInDbAsync(async db =>
        {
            Order persisted = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.SourceRef == order.Id);
            Assert.Equal(OrderStatus.PAID, persisted.Status);
            Assert.Equal("rebill-after-paid", persisted.RebillId);
            Assert.Equal("rebill-after-paid", grant.RebillId);
            Assert.NotNull(grant.NextChargeAt);
        });
    }

    [Fact]
    public async Task RunOnceAsync_RebillRecovery_NoActiveCard_DoesNotInventTokenOrCharge()
    {
        await AssertAmbiguousRebillRecoveryAsync(
            [new TBankCard { CardId = "deleted", Status = "D", RebillId = "old" }]);
    }

    [Fact]
    public async Task RunOnceAsync_RebillRecovery_MultipleActiveCards_DoesNotChooseOrCharge()
    {
        await AssertAmbiguousRebillRecoveryAsync(
        [
            new TBankCard { CardId = "one", Status = "A", RebillId = "rebill-one" },
            new TBankCard { CardId = "two", Status = "A", RebillId = "rebill-two" },
        ]);
    }

    [Fact]
    public async Task RunOnceAsync_CheckOrderFailClosed_EmptyHistoryDoesNotMutateOrder()
    {
        await AssertCheckOrderFailClosedAsync(orderId => new TBankCheckOrderResponse
        {
            Success = true,
            OrderId = orderId,
            Payments = [],
        });
    }

    [Fact]
    public async Task RunOnceAsync_CheckOrderFailClosed_AmountMismatchDoesNotMutateOrder()
    {
        await AssertCheckOrderFailClosedAsync(orderId => SuccessfulCheckOrder(
            orderId,
            DEFAULT_AMOUNT_CENTS + 1,
            "wrong-amount",
            "CONFIRMED"));
    }

    [Fact]
    public async Task RunOnceAsync_CheckOrderFailClosed_MultipleMatchingPaymentsDoesNotMutateOrder()
    {
        await AssertCheckOrderFailClosedAsync(orderId => new TBankCheckOrderResponse
        {
            Success = true,
            OrderId = orderId,
            Payments =
            [
                Payment("first", DEFAULT_AMOUNT_CENTS, "CONFIRMED"),
                Payment("second", DEFAULT_AMOUNT_CENTS, "CONFIRMED"),
            ],
        });
    }

    [Fact]
    public async Task RunOnceAsync_CheckOrderFailClosed_ExactAndUnrelatedPaymentsDoesNotMutateOrder()
    {
        await AssertCheckOrderFailClosedAsync(orderId => new TBankCheckOrderResponse
        {
            Success = true,
            OrderId = orderId,
            Payments =
            [
                Payment("exact", DEFAULT_AMOUNT_CENTS, "CONFIRMED"),
                Payment("different-amount", DEFAULT_AMOUNT_CENTS + 1, "CONFIRMED"),
            ],
        });
    }

    [Fact]
    public async Task RunOnceAsync_CheckOrderFailClosed_PoisonOldestDoesNotStarveNextBatch()
    {
        Order poison = await SeedAgedPendingOrderAsync(ageMinutes: 10, attachExternalRef: false);
        Order recoverable = await SeedAgedPendingOrderAsync(ageMinutes: 5, attachExternalRef: false);
        Factory.TBankClient.CheckOrderHandler = orderId => orderId == poison.Id.ToString()
            ? new TBankCheckOrderResponse { Success = true, OrderId = orderId, Payments = [] }
            : SuccessfulCheckOrder(
                orderId,
                recoverable.AmountCents,
                "payment-after-poison",
                "CONFIRMED");

        var options = new ReconciliationOptions
        {
            IntervalSeconds = 30,
            MinAgeSecondsBeforePoll = 120,
            MaxPendingHoursBeforeExpire = 24,
            BatchSize = 1,
            RetryDeferralSeconds = 300,
        };
        var service = new PendingOrderReconciliationService(
            Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor(options),
            Services.GetRequiredService<ILogger<PendingOrderReconciliationService>>());

        await service.RunOnceAsync(CancellationToken.None);
        await ExecuteInDbAsync(async db =>
        {
            DateTimeOffset eligibleAgain = DateTimeOffset.UtcNow.AddMinutes(-10);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE access.order_events SET created_at = {eligibleAgain} WHERE order_id = {poison.Id} AND event_type = 'RECONCILIATION_DEFERRED'");
        });
        await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal([poison.Id.ToString(), recoverable.Id.ToString()], Factory.TBankClient.CheckOrderCalls);
        await ExecuteInDbAsync(async db => Assert.Equal(
            OrderStatus.PAID,
            (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == recoverable.Id)).Status));
    }

    [Fact]
    public async Task RunOnceAsync_RebillRecovery_PoisonOldestDoesNotStarveNextBatch()
    {
        Order poison = await SeedAgedPendingOrderAsync(
            ageMinutes: 10, subscription: true, attachExternalRef: true);
        Order recoverable = await SeedAgedPendingOrderAsync(
            ageMinutes: 5, subscription: true, attachExternalRef: true);
        await MarkPaidWithoutRebillAsync(poison);
        await MarkPaidWithoutRebillAsync(recoverable);
        Factory.TBankClient.GetCardListHandler = customerKey => customerKey == poison.UserId.ToString()
            ? Array.Empty<TBankCard>()
            : [new TBankCard { CardId = "active", Status = "A", RebillId = "rebill-after-poison" }];

        var options = new ReconciliationOptions
        {
            IntervalSeconds = 30,
            MinAgeSecondsBeforePoll = 120,
            MaxPendingHoursBeforeExpire = 24,
            BatchSize = 1,
            RetryDeferralSeconds = 300,
        };
        var service = new PendingOrderReconciliationService(
            Services.GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor(options),
            Services.GetRequiredService<ILogger<PendingOrderReconciliationService>>());

        await service.RunOnceAsync(CancellationToken.None);
        await ExecuteInDbAsync(async db =>
        {
            DateTimeOffset eligibleAgain = DateTimeOffset.UtcNow.AddMinutes(-10);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE access.order_events SET created_at = {eligibleAgain} WHERE order_id = {poison.Id} AND event_type = 'RECONCILIATION_DEFERRED'");
        });
        await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(
            [poison.UserId.ToString(), recoverable.UserId.ToString()],
            Factory.TBankClient.GetCardListCalls);
        await ExecuteInDbAsync(async db => Assert.Equal(
            "rebill-after-poison",
            (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == recoverable.Id)).RebillId));
    }

    [Fact]
    public void CheckOrderFailClosed_ConflictingLocalPaymentIdDoesNotReplaceIt()
    {
        Order order = Order.Create(
            Guid.NewGuid(), Guid.NewGuid(), DEFAULT_AMOUNT_CENTS, "RUB", "tbank").Value;
        Assert.True(order.AttachExternalRef("local-payment").IsSuccess);
        TBankCheckOrderResponse response = SuccessfulCheckOrder(
            order.Id.ToString(), order.AmountCents, "different-provider-payment", "CONFIRMED");

        Result<TBankPaymentHistory, Error> result = TBankOrderRecovery.MatchAndAttach(order, response);

        Assert.True(result.IsFailure);
        Assert.Equal("tbank.check_order.payment_id_conflict", result.Error.Messages[0].Code);
        Assert.Equal("local-payment", order.ExternalProviderRef);
    }

    /// <summary>
    /// Создаёт PENDING <see cref="Order"/> + LIFETIME_ALL <see cref="Plan"/>, затем
    /// raw-SQL'ом сдвигает <c>created_at</c> назад на <paramref name="ageMinutes"/>.
    /// EF Core ставит <c>CreatedAt = DateTimeOffset.UtcNow</c> в ctor — без override
    /// мы не можем seed'ить «старые» orders для теста age-cutoff'а.
    /// </summary>
    private async Task<Order> SeedAgedPendingOrderAsync(
        int ageMinutes,
        bool subscription = false,
        bool attachExternalRef = true)
    {
        Guid authorId = Guid.NewGuid();
        Plan plan = Plan.Create(
            authorId: authorId,
            tier: subscription ? PlanTier.SUBSCRIPTION : PlanTier.FULL_ALL,
            slug: PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Test plan").Value,
            courseIds: [],
            requestedCapabilities: null,
            term: subscription ? PlanTerm.Recurring(30) : null).Value;

        Guid userId = Guid.NewGuid();
        Order order = Order.Create(userId, plan.Id, DEFAULT_AMOUNT_CENTS, "RUB", "tbank").Value;
        if (attachExternalRef)
        {
            UnitResult<Error> attachResult = order.AttachExternalRef(
                DEFAULT_PAYMENT_ID + Guid.NewGuid().ToString("N")[..6]);
            Assert.True(attachResult.IsSuccess);
        }

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });

        if (ageMinutes > 0)
        {
            DateTimeOffset target = DateTimeOffset.UtcNow.AddMinutes(-ageMinutes);
            await ExecuteInDbAsync(async db =>
            {
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE access.orders SET created_at = {target} WHERE id = {order.Id}");
            });
        }

        return order;
    }

    private async Task AssertCheckOrderFailClosedAsync(
        Func<string, TBankCheckOrderResponse> responseFactory)
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5, attachExternalRef: false);
        Factory.TBankClient.CheckOrderHandler = orderId => responseFactory(orderId);

        await Services.GetRequiredService<PendingOrderReconciliationService>()
            .RunOnceAsync(CancellationToken.None);

        Assert.Equal(order.Id.ToString(), Assert.Single(Factory.TBankClient.CheckOrderCalls));
        Assert.Empty(Factory.TBankClient.InitCalls);
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        await ExecuteInDbAsync(async db =>
        {
            Order persisted = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PENDING, persisted.Status);
            Assert.Null(persisted.ExternalProviderRef);
            Assert.Equal(0, await db.PlanGrants.CountAsync(g => g.SourceRef == order.Id));
        });
    }

    private async Task AssertAmbiguousRebillRecoveryAsync(IReadOnlyList<TBankCard> cards)
    {
        Order order = await SeedAgedPendingOrderAsync(ageMinutes: 5, subscription: true);
        Factory.TBankClient.GetStateHandler = paymentId => new TBankGetStateResponse
        {
            Success = true,
            Status = "CONFIRMED",
            PaymentId = paymentId,
            OrderId = order.Id.ToString(),
            Amount = order.AmountCents,
        };
        Factory.TBankClient.GetCardListHandler = _ => Result.Success<IReadOnlyList<TBankCard>, Error>(cards);

        await Services.GetRequiredService<PendingOrderReconciliationService>()
            .RunOnceAsync(CancellationToken.None);

        Assert.Empty(Factory.TBankClient.ChargeCalls);
        await ExecuteInDbAsync(async db =>
        {
            Order persisted = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.SourceRef == order.Id);
            Assert.Equal(OrderStatus.PAID, persisted.Status);
            Assert.Null(persisted.RebillId);
            Assert.Null(grant.RebillId);
            Assert.Null(grant.NextChargeAt);
        });
    }

    private async Task MarkPaidWithoutRebillAsync(Order order)
    {
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<
            Core.Features.Billing.UseCases.PaymentWebhookHandler>();
        var transactions = scope.ServiceProvider.GetRequiredService<ITransactionManager>();
        UnitResult<Error> handled = await handler.Handle(
            new Contracts.Billing.PaymentWebhookRequest(
                order.Id,
                order.ExternalProviderRef!,
                "PAID",
                null,
                RebillId: null),
            CancellationToken.None);
        Assert.True(handled.IsSuccess);
        Assert.True((await transactions.SaveChangesAsync(CancellationToken.None)).IsSuccess);
    }

    private static TBankCheckOrderResponse SuccessfulCheckOrder(
        string orderId,
        long amount,
        string paymentId,
        string status) => new()
        {
            Success = true,
            OrderId = orderId,
            Payments = [Payment(paymentId, amount, status)],
        };

    private sealed class StaticOptionsMonitor(ReconciliationOptions value)
        : IOptionsMonitor<ReconciliationOptions>
    {
        public ReconciliationOptions CurrentValue { get; } = value;

        public ReconciliationOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<ReconciliationOptions, string?> listener) => null;
    }

    private static TBankPaymentHistory Payment(string paymentId, long amount, string status) => new()
    {
        PaymentId = paymentId,
        Amount = amount,
        Status = status,
        Success = true,
    };
}
