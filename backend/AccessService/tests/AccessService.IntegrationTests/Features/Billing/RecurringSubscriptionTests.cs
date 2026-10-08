using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Billing;
using AccessService.Contracts.Plans.Requests;
using AccessService.Core.Features.Billing.TBank.Contracts;
using AccessService.Core.Features.Billing.UseCases;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

/// <summary>
/// T-Bank recurrent subscription plumbing (#614, A2a):
/// (a) ordering a SUBSCRIPTION plan sends Recurrent='Y' + CustomerKey on the Init request;
/// (b) a confirmed subscription webhook stores RebillId on the grant + sets a renewal-aligned
///     ExpiresAt + NextChargeAt (AttachRecurring).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class RecurringSubscriptionTests : AccessServiceTestsBase
{
    private const int IntervalDays = 30;

    public RecurringSubscriptionTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task POST_orders_SubscriptionPlan_SendsRecurrentAndCustomerKeyToInit()
    {
        Guid planId = await SeedPublicSubscriptionPlanAsync(slug: "sub-recurrent", priceCents: 99_000);

        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        // #674: SUBSCRIPTION plans are TRAINER-scoped → purchased via the dedicated trainer
        // endpoint (same CreateOrderHandler, same recurrent Init plumbing). The platform
        // /access/orders/ endpoint now rejects them.
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/trainer-pro/orders", new CreateOrderRequest(planId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Assert on the mocked ITBankClient.InitAsync request — subscription orders must
        // flag the parent recurrent payment + a stable CustomerKey (the userId string).
        var initCall = Assert.Single(Factory.TBankClient.InitCalls);
        Assert.Equal("Y", initCall.Recurrent);
        Assert.Equal(userId.ToString(), initCall.CustomerKey);
        Assert.Equal("1", initCall.DATA!["OperationInitiatorType"]);
    }

    [Fact]
    public async Task POST_orders_NonSubscriptionPlan_DoesNotSendRecurrent()
    {
        // Regression guard: ordinary (FULL_ALL) plans must NOT become recurrent.
        Guid planId = await SeedPublicFullAccessPlanAsync(slug: "non-recurrent", priceCents: 500_000);
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(planId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var initCall = Assert.Single(Factory.TBankClient.InitCalls);
        Assert.Null(initCall.Recurrent);
        Assert.Null(initCall.CustomerKey);
        Assert.Equal("0", initCall.DATA!["OperationInitiatorType"]);
    }

    [Fact]
    public async Task PAID_SubscriptionWithRebill_StoresRecurringTokensAndRenewalExpiry()
    {
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateSubscriptionPlanAsync(authorId: Guid.NewGuid());
        Guid orderId = await CreateOrderAsync(userId, plan.Id);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        PaymentWebhookHandler handler = ResolveHandler();
        UnitResult<Error> result = await handler.Handle(
            new PaymentWebhookRequest(orderId, "pay-100", "PAID", null, RebillId: "rebill-100"),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking()
                .FirstAsync(g => g.UserId == userId && g.PlanId == plan.Id);

            Assert.Equal(PlanGrantSource.PURCHASE, grant.Source);
            Assert.Equal(orderId, grant.SourceRef);

            // AttachRecurring saved RebillId + CustomerKey (= userId string).
            Assert.Equal("rebill-100", grant.RebillId);
            Assert.Equal(userId.ToString(), grant.CustomerKey);
            Assert.Equal(0, grant.ChargeFailureCount);

            // ExpiresAt = now + interval; NextChargeAt follows T-24h (#746).
            Assert.NotNull(grant.ExpiresAt);
            Assert.NotNull(grant.NextChargeAt);
            Assert.Equal(
                SubscriptionRenewalPolicy.FirstChargeAt(grant.ExpiresAt!.Value),
                grant.NextChargeAt);
            Assert.InRange(
                grant.ExpiresAt!.Value,
                before.AddDays(IntervalDays).AddSeconds(-5),
                after.AddDays(IntervalDays).AddSeconds(5));
        });
    }

    [Fact]
    public async Task PAID_SubscriptionWithoutRebill_GrantsAccessButNoAutoRenew()
    {
        // Degrade-to-manual: if T-Bank confirms without RebillId, still issue the grant
        // (with a renewal-aligned ExpiresAt) but leave recurring tokens unset.
        await Factory.ResetDatabaseAsync();

        Guid userId = Guid.NewGuid();
        Plan plan = await CreateSubscriptionPlanAsync(authorId: Guid.NewGuid());
        Guid orderId = await CreateOrderAsync(userId, plan.Id);

        PaymentWebhookHandler handler = ResolveHandler();
        UnitResult<Error> result = await handler.Handle(
            new PaymentWebhookRequest(orderId, "pay-200", "PAID", null, RebillId: null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);

        await ExecuteInDbAsync(async db =>
        {
            PlanGrant grant = await db.PlanGrants.AsNoTracking()
                .FirstAsync(g => g.UserId == userId && g.PlanId == plan.Id);

            Assert.Equal(PlanGrantStatus.ACTIVE, grant.Status);
            Assert.NotNull(grant.ExpiresAt); // access still time-bounded
            Assert.Null(grant.RebillId);
            Assert.Null(grant.CustomerKey);
            Assert.Null(grant.NextChargeAt);
        });
    }

    private PaymentWebhookHandler ResolveHandler()
    {
        IServiceScope scope = Factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<PaymentWebhookHandler>();
    }

    private async Task<Plan> CreateSubscriptionPlanAsync(Guid authorId)
    {
        Plan plan = Plan.Create(
            authorId: authorId,
            tier: PlanTier.SUBSCRIPTION,
            slug: PlanSlug.Of($"sub-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Trainer Pro").Value,
            courseIds: [],
            requestedCapabilities: null,
            offerType: null,
            trialDurationDays: null,
            term: PlanTerm.Recurring(IntervalDays)).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });

        return plan;
    }

    private async Task<Guid> CreateOrderAsync(Guid userId, Guid planId)
    {
        Order order = Order.Create(userId, planId, amountCents: 99_000, currency: "RUB").Value;
        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });
        return order.Id;
    }

    /// <summary>Creates + publishes a SUBSCRIPTION plan via the API as the default author.</summary>
    private async Task<Guid> SeedPublicSubscriptionPlanAsync(string slug, int priceCents)
    {
        AuthenticateAs("platform-author");

        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.SUBSCRIPTION),
            Slug: slug,
            DisplayName: "Trainer Pro",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: priceCents,
            Currency: "RUB",
            CourseIds: [],
            DisplayOrder: 0,
            RecurringIntervalDays: IntervalDays);

        HttpResponseMessage create = await AppHttpClient.PostAsJsonAsync("/access/plans/", request);
        create.EnsureSuccessStatusCode();
        Guid planId = (await create.Content.ReadFromJsonAsync<Envelope<Guid>>())!.Result;

        HttpResponseMessage publish = await AppHttpClient.PostAsync(
            $"/access/plans/{planId}/publish", content: null);
        publish.EnsureSuccessStatusCode();
        return planId;
    }

    private async Task<Guid> SeedPublicFullAccessPlanAsync(string slug, int priceCents)
    {
        AuthenticateAs("platform-author");

        CreatePlanRequest request = new(
            Tier: nameof(PlanTier.FULL_ALL),
            Slug: slug,
            DisplayName: "Полный доступ",
            ShortDescription: null,
            LongDescription: null,
            CoverFileId: null,
            Features: null,
            PriceCents: priceCents,
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
