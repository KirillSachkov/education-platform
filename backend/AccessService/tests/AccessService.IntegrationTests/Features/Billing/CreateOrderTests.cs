using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AccessService.Contracts.Billing;
using AccessService.Contracts.Plans.Requests;
using AccessService.Contracts.TrainerPro;
using AccessService.Core.Domain;
using AccessService.Core.Features.Billing.TBank;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Core.Features.Billing.Reconciliation;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CreateOrderTests : AccessServiceTestsBase
{
    public CreateOrderTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task POST_orders_HappyPath_CreatesPendingOrderAndReturnsPaymentUrl()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "happy-path", priceCents: 500_000);
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<CreateOrderResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>();
        Assert.NotNull(envelope);
        Assert.False(envelope!.IsError);
        CreateOrderResponse body = envelope.Result!;
        Assert.NotEqual(Guid.Empty, body.OrderId);
        Assert.StartsWith("https://", body.PaymentUrl, StringComparison.Ordinal);

        await ExecuteInDbAsync(async db =>
        {
            Order? order = await db.Orders.FirstOrDefaultAsync(o => o.Id == body.OrderId);
            Assert.NotNull(order);
            Assert.Equal(OrderStatus.PENDING, order!.Status);
            Assert.Equal(500_000, order.AmountCents);
            Assert.Equal("RUB", order.Currency);
            Assert.Equal("tbank", order.Provider);
            Assert.NotNull(order.ExternalProviderRef);
        });
    }

    [Fact]
    public async Task POST_orders_PriceTampering_IgnoresClientAmount_UsesPlanPrice()
    {
        // CreateOrderRequest only carries PlanId — server snapshots price from
        // Plan.PriceCents. This test verifies the contract by checking the persisted
        // amount matches the seeded plan price exactly.
        Guid planId = await SeedPublicPlanAsync(slug: "price-snapshot", priceCents: 100_000);
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<CreateOrderResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>();
        CreateOrderResponse body = envelope!.Result!;

        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.FirstAsync(o => o.Id == body.OrderId);
            Assert.Equal(100_000, order.AmountCents);
        });
    }

    [Fact]
    public async Task POST_orders_DuringActivePromotion_ChargesDiscountedPrice()
    {
        // Author seeds + publishes a 990_000 plan, then sets a 30% promotion.
        Guid planId = await SeedPublicPlanAsync(slug: "promo-order", priceCents: 990_000);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HttpResponseMessage promo = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{planId}/promotion/",
            new SetPromotionRequest(30, now.AddMinutes(-1), now.AddDays(7)));
        promo.EnsureSuccessStatusCode();

        // A participant buys during the active window — Order snapshots effective price.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<CreateOrderResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>();
        CreateOrderResponse body = envelope!.Result!;

        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.FirstAsync(o => o.Id == body.OrderId);
            Assert.Equal(693_000, order.AmountCents); // 990000 - 30%
        });
    }

    [Fact]
    public async Task POST_orders_WithUpgradeCredit_ChargesFinalPriceNotFull()
    {
        // #486: Order.AmountCents = эффективная цена МИНУС upgrade-credit. Сумма «К оплате»
        // на pricing-странице (quote) и реальное списание обязаны совпадать.
        Guid coursePlanId = await SeedPublicCoursePlanAsync("credit-course", priceCents: 200_000);
        Guid fullPlanId = await SeedPublicPlanAsync(slug: "credit-full", priceCents: 500_000);

        Guid userId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(PlanGrant.Create(
                userId,
                coursePlanId,
                PlanGrantSource.PURCHASE,
                sourceRef: Guid.NewGuid(),
                expiresAt: null,
                pricePaidCents: 200_000));
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(fullPlanId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CreateOrderResponse body =
            (await response.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>())!.Result!;

        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.FirstAsync(o => o.Id == body.OrderId);
            Assert.Equal(300_000, order.AmountCents); // 500000 − 200000 credit
        });
    }

    [Fact]
    public async Task POST_orders_AlreadyOwnedPlan_Returns400_NoOrder()
    {
        // #486: server-side guard — ACTIVE grant на target-план блокирует повторную покупку
        // (фронт прячет кнопку, но доверять ему нельзя).
        Guid planId = await SeedPublicPlanAsync(slug: "owned-order", priceCents: 500_000);

        Guid userId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(PlanGrant.Create(
                userId,
                planId,
                PlanGrantSource.ADMIN_GRANT,
                sourceRef: null,
                expiresAt: null,
                pricePaidCents: null));
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "order.plan.already_owned", StringComparison.Ordinal));

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.Orders.CountAsync(o => o.PlanId == planId);
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public async Task POST_orders_CreditCoversFullPrice_Returns400_NothingToPay()
    {
        // Unpaid ACTIVE grant на план той же цены: fallback-credit покрывает 100% цены —
        // нулевой заказ в T-Bank невозможен, оформление через автора вручную.
        Guid coursePlanId = await SeedPublicCoursePlanAsync("full-credit-course", priceCents: 500_000);
        Guid fullPlanId = await SeedPublicPlanAsync(slug: "full-credit-target", priceCents: 500_000);

        Guid userId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(PlanGrant.Create(
                userId,
                coursePlanId,
                PlanGrantSource.INVITE_LINK,
                sourceRef: Guid.NewGuid(),
                expiresAt: null,
                pricePaidCents: null));
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(fullPlanId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "order.nothing_to_pay", StringComparison.Ordinal));

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.Orders.CountAsync(o => o.PlanId == fullPlanId);
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public async Task POST_orders_IdempotencyKey_ReplaySameKey_ReturnsSameResponse()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "idempotent", priceCents: 500_000);
        AuthenticateAs("platform-participant", Guid.NewGuid());
        string idempotencyKey = Guid.NewGuid().ToString();

        HttpRequestMessage req1 = new(HttpMethod.Post, "/access/orders/")
        {
            Content = JsonContent.Create(new CreateOrderRequest(planId)),
        };
        req1.Headers.Add("Idempotency-Key", idempotencyKey);
        HttpResponseMessage resp1 = await AppHttpClient.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        CreateOrderResponse body1 =
            (await resp1.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>())!.Result!;

        HttpRequestMessage req2 = new(HttpMethod.Post, "/access/orders/")
        {
            Content = JsonContent.Create(new CreateOrderRequest(planId)),
        };
        req2.Headers.Add("Idempotency-Key", idempotencyKey);
        HttpResponseMessage resp2 = await AppHttpClient.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        CreateOrderResponse body2 =
            (await resp2.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>())!.Result!;

        // Same OrderId, same PaymentUrl — replayed cached response.
        Assert.Equal(body1.OrderId, body2.OrderId);
        Assert.Equal(body1.PaymentUrl, body2.PaymentUrl);

        // Only one Order persisted (replay short-circuited before handler ran).
        await ExecuteInDbAsync(async db =>
        {
            int count = await db.Orders.CountAsync(o => o.PlanId == planId);
            Assert.Equal(1, count);
        });
    }

    [Fact]
    public async Task POST_orders_IdempotencyKey_ConcurrentSameUserAndKey_CreatesOneOrderAndCallsInitOnce()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "idempotent-concurrent", priceCents: 500_000);
        AuthenticateAs("platform-participant", Guid.NewGuid());
        string key = Guid.NewGuid().ToString();

        using var firstInitEntered = new ManualResetEventSlim();
        using var releaseFirstInit = new ManualResetEventSlim();
        int initAttempts = 0;
        Factory.TBankClient.InitHandler = request =>
        {
            if (Interlocked.Increment(ref initAttempts) == 1)
            {
                firstInitEntered.Set();
                Assert.True(releaseFirstInit.Wait(TimeSpan.FromSeconds(10)));
            }

            return SuccessfulInit(request);
        };

        Task<HttpResponseMessage> first = AppHttpClient.SendAsync(CreateOrderRequestMessage(planId, key));
        Assert.True(firstInitEntered.Wait(TimeSpan.FromSeconds(10)));
        Task<HttpResponseMessage> second = AppHttpClient.SendAsync(CreateOrderRequestMessage(planId, key));

        HttpResponseMessage secondResponse = await second.WaitAsync(TimeSpan.FromSeconds(10));
        releaseFirstInit.Set();
        HttpResponseMessage firstResponse = await first.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(
            new[] { firstResponse.StatusCode, secondResponse.StatusCode },
            status => status == HttpStatusCode.OK);
        Assert.Contains(
            new[] { firstResponse.StatusCode, secondResponse.StatusCode },
            status => status == HttpStatusCode.Conflict);
        Assert.Equal(1, initAttempts);
        await ExecuteInDbAsync(async db =>
            Assert.Equal(1, await db.Orders.CountAsync(o => o.PlanId == planId)));
    }

    [Fact]
    public async Task POST_orders_IdempotencyKey_SameUserAndKeyDifferentPlan_Returns409WithoutSecondInit()
    {
        Guid firstPlanId = await SeedPublicPlanAsync(slug: "idempotent-plan-a", priceCents: 500_000);
        Guid secondPlanId = await SeedPublicCoursePlanAsync(slug: "idempotent-plan-b", priceCents: 600_000);
        AuthenticateAs("platform-participant", Guid.NewGuid());
        string key = Guid.NewGuid().ToString();

        HttpResponseMessage first = await AppHttpClient.SendAsync(CreateOrderRequestMessage(firstPlanId, key));
        HttpResponseMessage mismatched = await AppHttpClient.SendAsync(CreateOrderRequestMessage(secondPlanId, key));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, mismatched.StatusCode);
        Envelope? envelope = await mismatched.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(
            envelope!.Error!.Messages,
            message => string.Equals(message.Code, "idempotency.request_mismatch", StringComparison.Ordinal));
        Assert.Single(Factory.TBankClient.InitCalls);
        await ExecuteInDbAsync(async db =>
            Assert.Equal(1, await db.Orders.CountAsync(o => o.PlanId == firstPlanId || o.PlanId == secondPlanId)));
    }

    [Fact]
    public async Task POST_orders_PersistsPendingOrderAndInitCalledBeforeExternalInit()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "durable-before-init", priceCents: 500_000);
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);
        string key = Guid.NewGuid().ToString();

        using var initEntered = new ManualResetEventSlim();
        using var releaseInit = new ManualResetEventSlim();
        Factory.TBankClient.InitHandler = request =>
        {
            initEntered.Set();
            Assert.True(releaseInit.Wait(TimeSpan.FromSeconds(10)));
            return SuccessfulInit(request);
        };

        Task<HttpResponseMessage> pendingRequest =
            AppHttpClient.SendAsync(CreateOrderRequestMessage(planId, key));
        Assert.True(initEntered.Wait(TimeSpan.FromSeconds(10)));

        bool durableBeforeInitReturned = await ExecuteInDbAsync(async db =>
        {
            Order? order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.PlanId == planId);
            IdempotencyKey? reservation = await db.IdempotencyKeys.AsNoTracking()
                .SingleOrDefaultAsync(i => i.UserId == userId && i.Key == key);
            return order is not null
                && order.Status == OrderStatus.PENDING
                && reservation is not null
                && reservation.PlanId == planId
                && reservation.ExpectedScope == PlanScope.PLATFORM
                && reservation.Status == IdempotencyKeyStatus.PROCESSING
                && reservation.OrderId == order.Id
                && reservation.ResponseBody is null
                && await db.OrderEvents.AsNoTracking().AnyAsync(
                    e => e.OrderId == order.Id && e.EventType == OrderEventType.INIT_CALLED);
        });

        releaseInit.Set();
        HttpResponseMessage response = await pendingRequest.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(durableBeforeInitReturned);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task POST_orders_AmbiguousInitRecovery_NetworkFailureRecoveredWithoutSecondInit()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "ambiguous-init", priceCents: 500_000);
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);
        string key = Guid.NewGuid().ToString();
        Factory.TBankClient.InitHandler = _ =>
            Error.Failure("tbank.network.error", "response lost after provider accepted Init");

        HttpResponseMessage response = await AppHttpClient.SendAsync(
            CreateOrderRequestMessage(planId, key));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        Order order = await ExecuteInDbAsync(async db =>
            await db.Orders.AsNoTracking().SingleAsync(o => o.PlanId == planId));
        Assert.Equal(OrderStatus.PENDING, order.Status);
        Assert.Null(order.ExternalProviderRef);

        Factory.TBankClient.CheckOrderHandler = orderId => new TBankCheckOrderResponse
        {
            Success = true,
            OrderId = orderId,
            Payments =
            [
                new TBankPaymentHistory
                {
                    PaymentId = "payment-ambiguous-init",
                    Amount = order.AmountCents,
                    Status = "CONFIRMED",
                    Success = true,
                },
            ],
        };
        await ExecuteInDbAsync(async db =>
            await db.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(update =>
                update.SetProperty(o => o.CreatedAt, DateTimeOffset.UtcNow.AddMinutes(-5))));

        await Services.GetRequiredService<PendingOrderReconciliationService>()
            .RunOnceAsync(CancellationToken.None);

        HttpResponseMessage retry = await AppHttpClient.SendAsync(
            CreateOrderRequestMessage(planId, key));

        Assert.Single(Factory.TBankClient.InitCalls);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(order.Id.ToString(), Assert.Single(Factory.TBankClient.CheckOrderCalls));
        await ExecuteInDbAsync(async db =>
        {
            Order persisted = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            Assert.Equal(OrderStatus.PAID, persisted.Status);
            Assert.Equal("payment-ambiguous-init", persisted.ExternalProviderRef);
            Assert.Equal(1, await db.PlanGrants.CountAsync(g => g.SourceRef == order.Id));
            Assert.Equal(
                IdempotencyKeyStatus.RECOVERED,
                (await db.IdempotencyKeys.AsNoTracking()
                    .SingleAsync(i => i.UserId == userId && i.Key == key)).Status);
        });
    }

    [Fact]
    public async Task POST_orders_IdempotencyKey_ScopedByUser_RetriesAreStableForBothUsers()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "idempotent-cross-user", priceCents: 500_000);
        string sharedKey = Guid.NewGuid().ToString();

        // FakeTBankClient hard-codes PaymentId="1234567890" — would violate
        // unique constraint ux_orders_external_provider_ref on the second order.
        // Override InitHandler so each call returns a fresh PaymentId.
        Factory.TBankClient.InitHandler = req => new TBankInitResponse
        {
            Success = true,
            ErrorCode = "0",
            Status = "NEW",
            PaymentId = Guid.NewGuid().ToString("N"),
            OrderId = req.OrderId,
            Amount = req.Amount,
            PaymentURL = $"https://securepayments.tinkoff.ru/x/{req.OrderId}",
            TerminalKey = req.TerminalKey,
        };

        // User A
        Guid userAId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userAId);
        HttpRequestMessage reqA = new(HttpMethod.Post, "/access/orders/")
        {
            Content = JsonContent.Create(new CreateOrderRequest(planId)),
        };
        reqA.Headers.Add("Idempotency-Key", sharedKey);
        HttpResponseMessage respA = await AppHttpClient.SendAsync(reqA);
        Assert.Equal(HttpStatusCode.OK, respA.StatusCode);
        CreateOrderResponse bodyA =
            (await respA.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>())!.Result!;

        // User B with SAME key — must get a fresh response, not replay user A's.
        Guid userBId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userBId);
        HttpRequestMessage reqB = new(HttpMethod.Post, "/access/orders/")
        {
            Content = JsonContent.Create(new CreateOrderRequest(planId)),
        };
        reqB.Headers.Add("Idempotency-Key", sharedKey);
        HttpResponseMessage respB = await AppHttpClient.SendAsync(reqB);
        Assert.Equal(HttpStatusCode.OK, respB.StatusCode);
        CreateOrderResponse bodyB =
            (await respB.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>())!.Result!;

        AuthenticateAs("platform-participant", userAId);
        HttpResponseMessage replayA = await AppHttpClient.SendAsync(CreateOrderRequestMessage(planId, sharedKey));
        Assert.Equal(HttpStatusCode.OK, replayA.StatusCode);
        CreateOrderResponse replayABody =
            (await replayA.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>())!.Result!;

        AuthenticateAs("platform-participant", userBId);
        HttpResponseMessage replayB = await AppHttpClient.SendAsync(CreateOrderRequestMessage(planId, sharedKey));
        Assert.Equal(HttpStatusCode.OK, replayB.StatusCode);
        CreateOrderResponse replayBBody =
            (await replayB.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>())!.Result!;

        // Different OrderIds — user B got a fresh response.
        Assert.NotEqual(bodyA.OrderId, bodyB.OrderId);
        Assert.Equal(bodyA, replayABody);
        Assert.Equal(bodyB, replayBBody);

        // Two distinct orders persisted.
        await ExecuteInDbAsync(async db =>
        {
            int count = await db.Orders.CountAsync(o => o.PlanId == planId);
            Assert.Equal(2, count);
        });
        Assert.Equal(2, Factory.TBankClient.InitCalls.Count);
    }

    [Fact]
    public async Task POST_orders_PlanArchived_Returns400()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "to-archive", priceCents: 500_000);
        // Archive after publish: API call as the author.
        HttpResponseMessage archive = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/archive", content: null);
        archive.EnsureSuccessStatusCode();

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_orders_PlanInactive_Returns400()
    {
        // No public API toggles IsActive without archiving. Seed via API + flip the
        // flag directly to exercise the second guard (after archive check).
        Guid planId = await SeedPublicPlanAsync(slug: "to-inactive", priceCents: 500_000);

        await ExecuteInDbAsync(async db =>
        {
            await db.Plans
                .Where(p => p.Id == planId)
                .ExecuteUpdateAsync(u => u.SetProperty(p => p.IsActive, false));
        });

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_orders_PlanNotPublic_Returns400()
    {
        // Create plan but don't publish.
        Guid planId = await CreateUnpublishedPlanAsync(slug: "not-public", priceCents: 500_000);

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_orders_PlanWithoutPrice_Returns400()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "no-price", priceCents: null);
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_orders_PlanNotFound_Returns404()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_orders_BillingDisabledViaToggle_Returns400_NoOrder()
    {
        // #414: даже при настроенном T-Bank приём оплаты можно выключить admin-тумблером
        // (ряд billing_config с IsEnabled=false). CreateOrder должен вернуть billing.disabled.
        Guid planId = await SeedPublicPlanAsync(slug: "billing-off", priceCents: 500_000);

        await ExecuteInDbAsync(async db =>
        {
            db.BillingConfigs.Add(
                AccessService.Domain.Billing.BillingConfig.Create(isEnabled: false, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });

        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "billing.disabled", StringComparison.Ordinal));

        await ExecuteInDbAsync(async db =>
        {
            int count = await db.Orders.CountAsync(o => o.PlanId == planId);
            Assert.Equal(0, count);
        });
    }

    [Fact]
    public async Task POST_orders_Anonymous_Returns401()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "anon", priceCents: 500_000);
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task POST_orders_HappyPath_RecordsInitAuditEventsWithCorrelation()
    {
        // #443: успешный init пишет INIT_CALLED + INIT_RESPONDED audit-rows, а заказ несёт
        // correlation_id (OTel trace_id запроса) — связка заказа с логами/трейсами.
        Guid planId = await SeedPublicPlanAsync(slug: "init-audit", priceCents: 500_000);
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CreateOrderResponse body =
            (await response.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>())!.Result!;

        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == body.OrderId);
            Assert.Equal(OrderStatus.PENDING, order.Status);
            // OTel trace_id captured during the request (W3C 32-hex).
            Assert.False(string.IsNullOrEmpty(order.CorrelationId));
            Assert.Equal(32, order.CorrelationId!.Length);

            List<OrderEvent> events = await db.OrderEvents.AsNoTracking()
                .Where(e => e.OrderId == order.Id)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            Assert.Contains(events, e => e.EventType == OrderEventType.INIT_CALLED);
            Assert.Contains(events, e => e.EventType == OrderEventType.INIT_RESPONDED);
            // Init events carry the same correlation id as the order.
            Assert.All(
                events.Where(e => e.EventType is OrderEventType.INIT_CALLED or OrderEventType.INIT_RESPONDED),
                e => Assert.Equal(order.CorrelationId, e.CorrelationId));
            // No PaymentURL leaked into the audit payload.
            Assert.DoesNotContain(events, e =>
                e.PayloadJson is not null && e.PayloadJson.Contains("https://", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task POST_orders_TBankInitFailure_PersistsFailedOrderWithInitFailedEvent()
    {
        // #443: payment-init сбой ДО редиректа теперь сохраняется как first-class FAILED-заказ
        // + INIT_FAILED audit-row (раньше откатывался — админка не видела сбой).
        Guid planId = await SeedPublicPlanAsync(slug: "init-fail-audit", priceCents: 500_000);
        AuthenticateAs("platform-participant", Guid.NewGuid());

        Factory.TBankClient.InitHandler = _ => Result.Failure<TBankInitResponse, Error>(
            Error.Failure("tbank.init.failed", "test failure"));

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));

        // Пользователь всё ещё получает ошибку — заказ не удался.
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.AsNoTracking().FirstAsync(o => o.PlanId == planId);
            Assert.Equal(OrderStatus.FAILED, order.Status);
            Assert.False(string.IsNullOrEmpty(order.CorrelationId));

            List<OrderEvent> events = await db.OrderEvents.AsNoTracking()
                .Where(e => e.OrderId == order.Id).ToListAsync();
            Assert.Contains(events, e => e.EventType == OrderEventType.INIT_CALLED);
            OrderEvent failed = events.Single(e => e.EventType == OrderEventType.INIT_FAILED);
            Assert.Equal(order.CorrelationId, failed.CorrelationId);
            // Sanitization: technical code only — no PaymentURL / secrets in payload.
            Assert.DoesNotContain("https://", failed.PayloadJson ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("Password", failed.PayloadJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task POST_orders_IdempotencyKey_AmbiguousInitFailure_RemainsProcessingWithoutSecondInit()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "init-ambiguous-idempotent", priceCents: 500_000);
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);
        string key = Guid.NewGuid().ToString();
        Factory.TBankClient.InitHandler = _ => TBankErrors.NetworkError("timeout");

        HttpResponseMessage first = await AppHttpClient.SendAsync(CreateOrderRequestMessage(planId, key));
        HttpResponseMessage retry = await AppHttpClient.SendAsync(CreateOrderRequestMessage(planId, key));

        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Single(Factory.TBankClient.InitCalls);
        await ExecuteInDbAsync(async db =>
        {
            IdempotencyKey state = await db.IdempotencyKeys.AsNoTracking()
                .SingleAsync(i => i.UserId == userId && i.Key == key);
            Assert.Equal(IdempotencyKeyStatus.PROCESSING, state.Status);
            Assert.Null(state.ResponseBody);
            Assert.Equal(
                OrderStatus.PENDING,
                (await db.Orders.AsNoTracking().SingleAsync(o => o.Id == state.OrderId)).Status);
            OrderEvent failed = await db.OrderEvents.AsNoTracking().SingleAsync(e =>
                e.OrderId == state.OrderId && e.EventType == OrderEventType.INIT_FAILED);
            using JsonDocument payload = JsonDocument.Parse(failed.PayloadJson!);
            Assert.True(payload.RootElement.GetProperty("ambiguous").GetBoolean());
        });
    }

    [Fact]
    public async Task POST_orders_IdempotencyKey_AmbiguousPendingReservationSurvivesCleanup()
    {
        Guid planId = await SeedPublicPlanAsync(slug: "init-ambiguous-cleanup", priceCents: 500_000);
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);
        string key = Guid.NewGuid().ToString();
        Factory.TBankClient.InitHandler = _ => TBankErrors.NetworkError("timeout");

        HttpResponseMessage first = await AppHttpClient.SendAsync(CreateOrderRequestMessage(planId, key));
        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            DateTimeOffset old = DateTimeOffset.UtcNow.AddHours(-25);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE access.idempotency_keys SET created_at = {old} WHERE user_id = {userId} AND key = {key}");
        });

        await Services.GetRequiredService<PendingOrderReconciliationService>()
            .RunOnceAsync(CancellationToken.None);
        HttpResponseMessage retry = await AppHttpClient.SendAsync(CreateOrderRequestMessage(planId, key));

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Single(Factory.TBankClient.InitCalls);
        await ExecuteInDbAsync(async db => Assert.Equal(
            IdempotencyKeyStatus.PROCESSING,
            (await db.IdempotencyKeys.AsNoTracking()
                .SingleAsync(i => i.UserId == userId && i.Key == key)).Status));
    }

    [Fact]
    public async Task POST_orders_UntrustedPaymentUrl_PersistsFailedOrderWithInitUrlRejectedEvent()
    {
        // #443/#440: Init вернул PaymentURL на недоверенном хосте → security-аудит INIT_URL_REJECTED,
        // заказ FAILED, юзер не редиректится. FakeTBankClient минует реальный guard, поэтому
        // эмулируем ровно ту ошибку, которую вернул бы TBankClient.
        Guid planId = await SeedPublicPlanAsync(slug: "init-url-rejected", priceCents: 500_000);
        AuthenticateAs("platform-participant", Guid.NewGuid());

        Factory.TBankClient.InitHandler = _ =>
            Result.Failure<TBankInitResponse, Error>(TBankErrors.UntrustedPaymentUrlHost());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.AsNoTracking().FirstAsync(o => o.PlanId == planId);
            Assert.Equal(OrderStatus.FAILED, order.Status);

            List<OrderEvent> events = await db.OrderEvents.AsNoTracking()
                .Where(e => e.OrderId == order.Id).ToListAsync();
            Assert.Single(events, e => e.EventType == OrderEventType.INIT_URL_REJECTED);
            // Отдельный security-сигнал — не обычный INIT_FAILED.
            Assert.DoesNotContain(events, e => e.EventType == OrderEventType.INIT_FAILED);
        });
    }

    [Fact]
    public async Task POST_orders_TrainerScopedPlan_Returns400_NoOrder()
    {
        // #674: the platform order endpoint must reject a TRAINER-scoped offer — it's purchasable
        // only via /access/trainer-pro/orders. Symmetric to the trainer endpoint rejecting platform plans.
        AuthenticateAs("platform-author");
        CreateTrainerProOfferRequest offer = new(
            Slug: "trainer-via-platform-order",
            DisplayName: "Тренажёр Pro",
            PriceCents: 49_000,
            RecurringIntervalDays: 30);
        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/admin/trainer-pro/offer", offer);
        create.EnsureSuccessStatusCode();
        Guid trainerPlanId = (await create.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;

        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(trainerPlanId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "order.plan.trainer_only", StringComparison.Ordinal));

        await ExecuteInDbAsync(async db =>
            Assert.Equal(0, await db.Orders.CountAsync(o => o.PlanId == trainerPlanId)));
    }

    /// <summary>
    /// Creates an active+published COURSE-plan (e.g. интенсив) as the default
    /// platform-author — donor of upgrade-credit in #486 tests.
    /// </summary>
    private async Task<Guid> SeedPublicCoursePlanAsync(string slug, int priceCents)
    {
        AuthenticateAs("platform-author");

        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.COURSE),
            Slug: slug,
            DisplayName: "Интенсив",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: priceCents,
            Currency: "RUB",
            CourseIds: [Guid.NewGuid()],
            DisplayOrder: 0);

        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        create.EnsureSuccessStatusCode();
        Guid planId = (await create.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;

        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();
        return planId;
    }

    private static HttpRequestMessage CreateOrderRequestMessage(Guid planId, string key)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "/access/orders/")
        {
            Content = JsonContent.Create(new CreateOrderRequest(planId)),
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static TBankInitResponse SuccessfulInit(TBankInitRequest request) => new()
    {
        Success = true,
        ErrorCode = "0",
        Status = "NEW",
        PaymentId = request.OrderId,
        OrderId = request.OrderId,
        Amount = request.Amount,
        PaymentURL = $"https://securepayments.tinkoff.ru/x/{request.OrderId}",
        TerminalKey = request.TerminalKey,
    };

    /// <summary>
    /// Creates an active+published plan as the default platform-author. Returns
    /// the plan id. Caller usually re-authenticates as the buying participant
    /// after seeding.
    /// </summary>
    private async Task<Guid> SeedPublicPlanAsync(string slug, int? priceCents)
    {
        Guid planId = await CreateUnpublishedPlanAsync(slug, priceCents);

        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();

        return planId;
    }

    private async Task<Guid> CreateUnpublishedPlanAsync(string slug, int? priceCents)
    {
        // Default identity in InitializeAsync is platform-author (DefaultUserId).
        AuthenticateAs("platform-author");

        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: "Тестовый план",
            ShortDescription: "Описание",
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: priceCents,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        create.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await create.Content.ReadFromJsonAsync<Envelope<Guid>>();
        Assert.NotNull(envelope);
        return envelope!.Result;
    }
}
