using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Billing;
using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Contracts.Plans.Requests;
using AccessService.Core.Features.Billing.UseCases;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using AccessService.Web.Jobs;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shared.Messaging.IntegrationEvents.Access.Events;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

/// <summary>
/// Paid trial month → credited upgrade (GitLab #580).
///
/// A trial plan = <c>Plan</c> tier <c>FULL_ALL</c> with <c>TrialDurationDays &gt; 0</c>, a price,
/// excluded from the FULL_ALL singleton index (coexists with a permanent FULL_ALL "lifetime" plan).
/// These tests assert the four acceptance criteria with the SPECIFIC money values:
/// <list type="number">
///   <item>Purchase → time-bounded <c>PlanGrant</c> (ExpiresAt = now + TrialDurationDays).</item>
///   <item>Expired trial grant loses access (ExpiredGrantsSweeper → EXPIRED + event).</item>
///   <item>One-shot guard — a 2nd order for the same trial plan is rejected.</item>
///   <item>Upgrade credits the trial price at any time unless the grant is revoked.</item>
///   <item>Trial purchase snapshots the lifetime FULL_ALL base price for future upgrade.</item>
/// </list>
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class TrialMonthTests : AccessServiceTestsBase
{
    private const int TrialDurationDays = 30;
    private const long TrialPriceCents = 1_500_000; // 15 000 ₽
    private const long LifetimePriceCents = 9_000_000; // 90 000 ₽
    private const int PromotionPercent = 20; // 20% off → exact integer
    private const long TrialDiscountedPriceCents = 1_200_000; // 1 500 000 − 20%

    public TrialMonthTests(IntegrationTestsWebFactory factory) : base(factory) { }

    // ───────────────────────────── AC#1 ─────────────────────────────

    [Fact]
    public async Task TrialPurchase_CreatesTimeBoundedGrant()
    {
        // AC#1: a PAID purchase of a trial plan creates a PlanGrant with
        // Source=PURCHASE, PricePaidCents == order amount, ExpiresAt ≈ now + 30 days.
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();

        Plan trialPlan = await SeedTrialPlanAsync(authorId);
        Guid orderId = await SeedOrderAsync(buyerId, trialPlan.Id, amountCents: TrialPriceCents);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        PaymentWebhookHandler handler = ResolveWebhookHandler();
        UnitResult<Error> result = await handler.Handle(
            new PaymentWebhookRequest(orderId, "ext-trial-1", "PAID", null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant? grant = await db.PlanGrants.AsNoTracking()
                .FirstOrDefaultAsync(g => g.UserId == buyerId && g.PlanId == trialPlan.Id);
            Assert.NotNull(grant);
            Assert.Equal(PlanGrantStatus.ACTIVE, grant!.Status);
            Assert.Equal(PlanGrantSource.PURCHASE, grant.Source);
            // Exact money value — the full price the buyer actually paid.
            Assert.Equal(TrialPriceCents, grant.PricePaidCents);

            // ExpiresAt is non-null and ~30 days out from the grant timestamp.
            Assert.NotNull(grant.ExpiresAt);
            DateTimeOffset expected = grant.GrantedAt.AddDays(TrialDurationDays);
            Assert.True(
                (grant.ExpiresAt!.Value - expected).Duration() < TimeSpan.FromSeconds(5),
                $"ExpiresAt {grant.ExpiresAt:O} should be ~{TrialDurationDays}d after GrantedAt {grant.GrantedAt:O}");

            // Sanity vs the request wall-clock: ExpiresAt sits within [before+30d, after+30d].
            Assert.InRange(
                grant.ExpiresAt!.Value,
                before.AddDays(TrialDurationDays).AddSeconds(-5),
                after.AddDays(TrialDurationDays).AddSeconds(5));
        });
    }

    [Fact]
    public async Task TrialPurchase_SnapshotsPlatformLifetimeUpgradeBasePrice()
    {
        Guid lifetimeAuthorId = Guid.NewGuid();
        Guid trialAuthorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();

        await SeedLifetimePlanAsync(lifetimeAuthorId, LifetimePriceCents);
        Plan trialPlan = await SeedTrialPlanAsync(trialAuthorId);
        Guid orderId = await SeedOrderAsync(buyerId, trialPlan.Id, amountCents: TrialPriceCents);

        PaymentWebhookHandler handler = ResolveWebhookHandler();
        UnitResult<Error> result = await handler.Handle(
            new PaymentWebhookRequest(orderId, "ext-trial-snapshot-1", "PAID", null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking()
                .SingleAsync(g => g.UserId == buyerId && g.PlanId == trialPlan.Id);

            Assert.Equal(TrialPriceCents, grant.PricePaidCents);
            Assert.Equal(LifetimePriceCents, grant.UpgradeBasePriceCents);
        });
    }

    // ──────────────────── AC#1 (cont.) — promo snapshot on the trial path (#580 follow-up) ────────────────────

    [Fact]
    public async Task TrialPurchaseDuringPromotion_SnapshotsDiscountedPrice()
    {
        // #580 follow-up: buying a trial plan WHILE it has an ACTIVE promotion must snapshot the
        // EFFECTIVE (discounted) price into Order.AmountCents (the existing #348 promo-snapshot
        // mechanic), and the resulting PlanGrant.PricePaidCents must equal that discounted amount —
        // so the later upgrade credit reflects what was actually paid, NOT the list price.
        // The trial TTL (now + TrialDurationDays) still applies on top of the discounted price.
        Guid buyerId = Guid.NewGuid();

        // Trial plan (FULL_ALL, TrialDurationDays=30) at a clean list price of 1 500 000.
        Guid trialPlanId = await CreateTrialPlanViaApiAsync();

        // Put a 20% promotion ACTIVE right now (starts yesterday, ends in 10 days) via the
        // PUT /access/plans/{id}/promotion/ endpoint — the same pattern CreateOrderTests uses.
        // CreateTrialPlanViaApiAsync authenticated as the owning author, so this PUT is owned.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        HttpResponseMessage promo = await AppHttpClient.PutAsJsonAsync(
            $"/access/plans/{trialPlanId}/promotion/",
            new SetPromotionRequest(PromotionPercent, now.AddDays(-1), now.AddDays(10)));
        promo.EnsureSuccessStatusCode();

        // Drive a real purchase through POST /access/orders/ (billing enabled in the test env,
        // FakeTBankClient) as the buyer.
        AuthenticateAs("platform-participant", buyerId);
        HttpResponseMessage orderResp = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(trialPlanId));
        Assert.Equal(HttpStatusCode.OK, orderResp.StatusCode);
        CreateOrderResponse orderBody =
            (await orderResp.Content.ReadFromJsonAsync<Envelope<CreateOrderResponse>>())!.Result!;

        // The Order snapshots the DISCOUNTED effective price, not the 1 500 000 list price.
        await ExecuteInDbAsync(async db =>
        {
            Order order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == orderBody.OrderId);
            Assert.Equal(TrialDiscountedPriceCents, order.AmountCents); // 1 200 000, NOT 1 500 000
        });

        // Webhook PAID → time-bounded PURCHASE grant priced at the discounted snapshot.
        DateTimeOffset before = DateTimeOffset.UtcNow;
        PaymentWebhookHandler handler = ResolveWebhookHandler();
        UnitResult<Error> result = await handler.Handle(
            new PaymentWebhookRequest(orderBody.OrderId, "ext-trial-promo-1", "PAID", null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant? grant = await db.PlanGrants.AsNoTracking()
                .FirstOrDefaultAsync(g => g.UserId == buyerId && g.PlanId == trialPlanId);
            Assert.NotNull(grant);
            Assert.Equal(PlanGrantStatus.ACTIVE, grant!.Status);
            Assert.Equal(PlanGrantSource.PURCHASE, grant.Source);
            // The credit anchor = what was actually paid (discounted), not the list price.
            Assert.Equal(TrialDiscountedPriceCents, grant.PricePaidCents);

            // Trial TTL still applies on top of the discount: ExpiresAt ≈ now + 30 days.
            Assert.NotNull(grant.ExpiresAt);
            DateTimeOffset expected = grant.GrantedAt.AddDays(TrialDurationDays);
            Assert.True(
                (grant.ExpiresAt!.Value - expected).Duration() < TimeSpan.FromSeconds(5),
                $"ExpiresAt {grant.ExpiresAt:O} should be ~{TrialDurationDays}d after GrantedAt {grant.GrantedAt:O}");
            Assert.InRange(
                grant.ExpiresAt!.Value,
                before.AddDays(TrialDurationDays).AddSeconds(-5),
                after.AddDays(TrialDurationDays).AddSeconds(5));
        });
    }

    // ───────────────────────────── AC#1 (cont.) ─────────────────────────────

    [Fact]
    public async Task ExpiredTrialGrant_LosesAccess()
    {
        // AC#1 cont.: an ACTIVE trial grant whose ExpiresAt is in the past is transitioned
        // to EXPIRED by the ExpiredGrantsSweeper, which publishes PlanGrantExpired.
        Guid authorId = Guid.NewGuid();
        Guid buyerId = Guid.NewGuid();

        Plan lifetimePlan = await SeedLifetimePlanAsync(authorId, LifetimePriceCents);
        Plan trialPlan = await SeedTrialPlanAsync(authorId);

        Guid grantId = await SeedTrialGrantAsync(
            buyerId,
            trialPlan.Id,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1), // already past
            pricePaidCents: TrialPriceCents,
            expire: false); // ACTIVE — the sweeper must flip it

        OutboxCollector.Clear();
        int swept = await RunExpiredGrantsSweeperAsync();

        Assert.Equal(1, swept);
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grantId);
            Assert.Equal(PlanGrantStatus.EXPIRED, grant.Status);
        });

        // Self-consume Redis sync (#79 path) is driven by this integration event.
        PlanGrantExpired published = Assert.Single(
            OutboxCollector.OfType<PlanGrantExpired>(), e => e.GrantId == grantId);
        Assert.Equal(buyerId, published.UserId);
        Assert.Equal(trialPlan.Id, published.PlanId);
        Assert.Equal(lifetimePlan.Id, published.CanonicalTelegramPlanId);
    }

    // ───────────────────────────── AC#2 ─────────────────────────────

    [Fact]
    public async Task SecondTrialPurchase_Rejected_OneShot_ActiveGrant()
    {
        // AC#2: a user with an ACTIVE trial grant on the trial plan cannot order it again.
        await AssertSecondTrialPurchaseRejectedAsync(expireFirstGrant: false);
    }

    [Fact]
    public async Task SecondTrialPurchase_Rejected_OneShot_ExpiredGrant()
    {
        // AC#2 variant: the one-shot guard also fires for an EXPIRED trial grant —
        // a used-up trial can't be re-bought as a cheap monthly subscription.
        await AssertSecondTrialPurchaseRejectedAsync(expireFirstGrant: true);
    }

    private async Task AssertSecondTrialPurchaseRejectedAsync(bool expireFirstGrant)
    {
        // Author + published trial plan; buyer already holds a trial grant on it.
        Guid trialPlanId = await CreateTrialPlanViaApiAsync();

        Guid buyerId = Guid.NewGuid();
        await SeedTrialGrantAsync(
            buyerId,
            trialPlanId,
            expiresAt: expireFirstGrant
                ? DateTimeOffset.UtcNow.AddDays(-1)
                : DateTimeOffset.UtcNow.AddDays(TrialDurationDays),
            pricePaidCents: TrialPriceCents,
            expire: expireFirstGrant);

        AuthenticateAs("platform-participant", buyerId);
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(trialPlanId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
        Assert.Contains(envelope!.Error!.Messages,
            m => string.Equals(m.Code, "order.trial.already_used", StringComparison.Ordinal));

        // No new order row was created for the buyer on this trial plan.
        await ExecuteInDbAsync(async db =>
        {
            int orders = await db.Orders.CountAsync(o => o.PlanId == trialPlanId && o.UserId == buyerId);
            Assert.Equal(0, orders);
            // Exactly the one seeded grant — no second grant materialized either.
            int grants = await db.PlanGrants.CountAsync(g => g.PlanId == trialPlanId && g.UserId == buyerId);
            Assert.Equal(1, grants);
        });
    }

    // ───────────────────────────── AC#3a ─────────────────────────────

    [Fact]
    public async Task Upgrade_WithinWindow_CreditsTrialPrice_ActiveTrial()
    {
        // AC#3a: ACTIVE trial grant (pricePaid=1.5M) → upgrade-quote on the lifetime plan
        // credits the trial price; final = lifetime − trial.
        Guid authorId = DefaultUserId; // default identity owns the plans (author-scoped credit)
        Guid trialPlanId = await CreateTrialPlanViaApiAsync();
        Guid lifetimePlanId = await CreateLifetimePlanViaApiAsync();

        Guid userId = Guid.NewGuid();
        await SeedTrialGrantAsync(
            userId,
            trialPlanId,
            expiresAt: DateTimeOffset.UtcNow.AddDays(TrialDurationDays), // active window
            pricePaidCents: TrialPriceCents,
            expire: false);

        UpgradeQuoteDto quote = await GetUpgradeQuoteAsync(lifetimePlanId, userId);

        Assert.Equal(LifetimePriceCents, quote.OriginalPriceCents);
        Assert.Equal(TrialPriceCents, quote.CreditCents);
        Assert.Equal(LifetimePriceCents - TrialPriceCents, quote.FinalPriceCents); // 7 500 000
        Assert.False(quote.IsOwned);
        UpgradeCreditSourceDto source = Assert.Single(quote.Sources);
        Assert.Equal(trialPlanId, source.PlanId);
        Assert.Equal(TrialPriceCents, source.CreditCents);
    }

    [Fact]
    public async Task Upgrade_AfterExpiry_CreditsTrialPrice()
    {
        // AC#3: trial grant EXPIRED 20 days ago → still credited. The paid month is
        // not a subscription renewal window; it is a credit toward lifetime full access.
        Guid trialPlanId = await CreateTrialPlanViaApiAsync();
        Guid lifetimePlanId = await CreateLifetimePlanViaApiAsync();

        Guid userId = Guid.NewGuid();
        await SeedTrialGrantAsync(
            userId,
            trialPlanId,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-20),
            pricePaidCents: TrialPriceCents,
            expire: true);

        UpgradeQuoteDto quote = await GetUpgradeQuoteAsync(lifetimePlanId, userId);

        Assert.Equal(LifetimePriceCents, quote.OriginalPriceCents);
        Assert.Equal(TrialPriceCents, quote.CreditCents);
        Assert.Equal(LifetimePriceCents - TrialPriceCents, quote.FinalPriceCents);
        UpgradeCreditSourceDto source = Assert.Single(quote.Sources);
        Assert.Equal(trialPlanId, source.PlanId);
        Assert.Equal(TrialPriceCents, source.CreditCents);
    }

    [Fact]
    public async Task Upgrade_AfterExpiredSweeperStillCreditsTrialPrice()
    {
        Guid trialPlanId = await CreateTrialPlanViaApiAsync();
        Guid lifetimePlanId = await CreateLifetimePlanViaApiAsync();

        Guid userId = Guid.NewGuid();
        Guid grantId = await SeedTrialGrantAsync(
            userId,
            trialPlanId,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-1),
            pricePaidCents: TrialPriceCents,
            expire: false);

        int swept = await RunExpiredGrantsSweeperAsync();
        Assert.Equal(1, swept);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking().SingleAsync(g => g.Id == grantId);
            Assert.Equal(PlanGrantStatus.EXPIRED, grant.Status);
        });

        UpgradeQuoteDto quote = await GetUpgradeQuoteAsync(lifetimePlanId, userId);

        Assert.Equal(LifetimePriceCents, quote.OriginalPriceCents);
        Assert.Equal(TrialPriceCents, quote.CreditCents);
        Assert.Equal(LifetimePriceCents - TrialPriceCents, quote.FinalPriceCents);
        Assert.False(quote.IsOwned);
        UpgradeCreditSourceDto source = Assert.Single(quote.Sources);
        Assert.Equal(trialPlanId, source.PlanId);
        Assert.Equal(TrialPriceCents, source.CreditCents);
    }

    // ───────────────────────────── AC#4 ─────────────────────────────

    [Fact]
    public async Task Upgrade_AfterLifetimePriceIncrease_UsesTrialPurchaseBasePriceSnapshot()
    {
        Guid trialPlanId = await CreateTrialPlanViaApiAsync();
        Guid lifetimePlanId = await CreateLifetimePlanViaApiAsync();

        Guid userId = Guid.NewGuid();
        await SeedTrialGrantAsync(
            userId,
            trialPlanId,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-20),
            pricePaidCents: TrialPriceCents,
            expire: true,
            upgradeBasePriceCents: LifetimePriceCents);

        await UpdatePlanPriceAsync(lifetimePlanId, priceCents: 11_200_000);

        UpgradeQuoteDto quote = await GetUpgradeQuoteAsync(lifetimePlanId, userId);

        Assert.Equal(LifetimePriceCents, quote.OriginalPriceCents);
        Assert.Equal(TrialPriceCents, quote.CreditCents);
        Assert.Equal(LifetimePriceCents - TrialPriceCents, quote.FinalPriceCents);
        UpgradeCreditSourceDto source = Assert.Single(quote.Sources);
        Assert.Equal(trialPlanId, source.PlanId);
        Assert.Equal(TrialPriceCents, source.CreditCents);
    }

    [Fact]
    public async Task AdminOverride_ForbiddenForNonOwnerNonAdmin()
    {
        // AC#4: the override endpoint requires plans.grant AND ownership — a participant
        // (no permission) and a foreign author (no ownership) are both rejected.
        Guid trialPlanId = await CreateTrialPlanViaApiAsync();

        Guid userId = Guid.NewGuid();
        await SeedTrialGrantAsync(
            userId,
            trialPlanId,
            expiresAt: DateTimeOffset.UtcNow.AddDays(-20),
            pricePaidCents: TrialPriceCents,
            expire: true);

        // Participant — lacks plans.grant → 403 at the permission gate.
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage participant = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{userId}/trial-credit-override/",
            new TrialCreditOverrideRequest(trialPlanId, Until: null));
        Assert.Equal(HttpStatusCode.Forbidden, participant.StatusCode);

        // Different author — has plans.grant but doesn't own this plan → 403 ownership.
        AuthenticateAs("platform-author", Guid.NewGuid());
        HttpResponseMessage foreignAuthor = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{userId}/trial-credit-override/",
            new TrialCreditOverrideRequest(trialPlanId, Until: null));
        Assert.Equal(HttpStatusCode.Forbidden, foreignAuthor.StatusCode);

        // The grant is untouched — no override leaked through.
        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking()
                .SingleAsync(g => g.PlanId == trialPlanId && g.UserId == userId);
            Assert.Null(grant.CreditOverrideUntil);
        });
    }

    [Fact]
    public async Task AdminOverride_NotFoundWhenUserHasNoTrialGrant()
    {
        // AC#4: override on a user who has NO trial grant on the plan → 404 (grant.not.found).
        Guid trialPlanId = await CreateTrialPlanViaApiAsync();

        AuthenticateAs("platform-author"); // owner of the plan
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/access/admin/users/{Guid.NewGuid()}/trial-credit-override/",
            new TrialCreditOverrideRequest(trialPlanId, Until: null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private PaymentWebhookHandler ResolveWebhookHandler()
    {
        IServiceScope scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PaymentWebhookHandler>();
    }

    /// <summary>
    /// Runs the <see cref="ExpiredGrantsSweeper"/> exactly once. The sweeper is a
    /// BackgroundService that is NOT hosted in the Testing environment, so we construct it
    /// directly (mirrors how integration tests instantiate it — see AccessService/CLAUDE.md).
    /// </summary>
    private async Task<int> RunExpiredGrantsSweeperAsync()
    {
        IOptionsMonitor<ExpiredGrantsSweeperOptions> options =
            Factory.Services.GetRequiredService<IOptionsMonitor<ExpiredGrantsSweeperOptions>>();
        ILogger<ExpiredGrantsSweeper> logger =
            Factory.Services.GetRequiredService<ILogger<ExpiredGrantsSweeper>>();

        ExpiredGrantsSweeper sweeper = new(Factory.Services, options, logger);
        return await sweeper.SweepOnceAsync(CancellationToken.None);
    }

    private async Task<UpgradeQuoteDto> GetUpgradeQuoteAsync(Guid planId, Guid userId)
    {
        AuthenticateAs("platform-participant", userId);
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/access/plans/{planId}/upgrade-quote/");
        resp.EnsureSuccessStatusCode();
        UpgradeQuoteDto? quote = (await resp.Content
            .ReadFromJsonAsync<Envelope<UpgradeQuoteDto>>())!.Result;
        Assert.NotNull(quote);
        return quote!;
    }

    /// <summary>
    /// Seeds (directly in the DB) a trial plan as an <see cref="PlanGrant"/> donor — used by
    /// the webhook/sweeper tests where no published catalog entry is required.
    /// </summary>
    private async Task<Plan> SeedTrialPlanAsync(Guid authorId)
    {
        Plan plan = Plan.Create(
            authorId,
            PlanTier.FULL_ALL,
            PlanSlug.Of($"trial-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Пробный месяц").Value,
            courseIds: [],
            requestedCapabilities: null,
            offerType: null,
            trialDurationDays: TrialDurationDays).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan;
    }

    private async Task<Plan> SeedLifetimePlanAsync(Guid authorId, long priceCents)
    {
        Plan plan = Plan.Create(
            authorId,
            PlanTier.FULL_ALL,
            PlanSlug.Of($"lifetime-{Guid.NewGuid():N}").Value,
            PlanDisplayName.Of("Полный доступ навсегда").Value,
            courseIds: [],
            requestedCapabilities: null,
            offerType: null,
            trialDurationDays: null).Value;
        plan.UpdatePrice(priceCents, "RUB");
        plan.Publish();

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan;
    }

    private async Task UpdatePlanPriceAsync(Guid planId, long priceCents)
    {
        AuthenticateAs("platform-author");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/access/plans/{planId}",
            new UpdatePlanRequest(
                DisplayName: null,
                ShortDescription: null,
                LongDescription: null,
                CoverFileId: null,
                Features: null,
                PriceCents: priceCents,
                Currency: "RUB",
                CourseIds: null,
                DisplayOrder: null));
        response.EnsureSuccessStatusCode();
    }

    private async Task<Guid> SeedOrderAsync(Guid userId, Guid planId, long amountCents)
    {
        Order order = Order.Create(userId, planId, amountCents, currency: "RUB", provider: "tbank").Value;
        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });
        return order.Id;
    }

    /// <summary>
    /// Seeds a PURCHASE-source trial grant straight into the DB (bypassing the webhook),
    /// optionally pre-transitioned to EXPIRED. <paramref name="expiresAt"/> is relative to
    /// real <c>UtcNow</c> — the calculator reads <c>TimeProvider.System</c>.
    /// </summary>
    private async Task<Guid> SeedTrialGrantAsync(
        Guid userId,
        Guid planId,
        DateTimeOffset expiresAt,
        long? pricePaidCents,
        bool expire,
        long? upgradeBasePriceCents = null)
    {
        PlanGrant grant = PlanGrant.Create(
            userId,
            planId,
            PlanGrantSource.PURCHASE,
            sourceRef: Guid.NewGuid(),
            expiresAt: expiresAt,
            pricePaidCents: pricePaidCents,
            upgradeBasePriceCents: upgradeBasePriceCents);
        if (expire)
        {
            grant.Expire();
        }

        await ExecuteInDbAsync(async db =>
        {
            db.PlanGrants.Add(grant);
            await db.SaveChangesAsync();
        });
        return grant.Id;
    }

    /// <summary>
    /// Creates + publishes a trial plan via the public API as the default platform-author.
    /// The trial plan is excluded from the FULL_ALL singleton index, so it can be published
    /// alongside the platform permanent FULL_ALL plan. #595: trial-длительность
    /// больше не приходит в запросе — задаётся <c>IsTrial=true</c>, срок берётся из конфига
    /// (<c>Access:TrialDurationDays</c>, default <see cref="TrialDurationDays"/>).
    /// </summary>
    private async Task<Guid> CreateTrialPlanViaApiAsync()
    {
        AuthenticateAs("platform-author");
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: $"trial-{Guid.NewGuid():N}",
            DisplayName: "Пробный месяц",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: TrialPriceCents,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0,
            IsTrial: true);

        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        create.EnsureSuccessStatusCode();
        Guid planId = (await create.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;

        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();
        return planId;
    }

    /// <summary>
    /// Creates + publishes a permanent (non-trial) FULL_ALL "lifetime" plan as the default
    /// platform-author — the upgrade target. <c>TrialDurationDays = null</c>.
    /// </summary>
    private async Task<Guid> CreateLifetimePlanViaApiAsync()
    {
        AuthenticateAs("platform-author");
        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: $"lifetime-{Guid.NewGuid():N}",
            DisplayName: "Полный доступ навсегда",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: LifetimePriceCents,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0);

        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        create.EnsureSuccessStatusCode();
        Guid planId = (await create.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;

        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();
        return planId;
    }
}
