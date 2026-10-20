using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Billing;
using AccessService.Contracts.Plans.Requests;
using AccessService.Domain;
using AccessService.Domain.Billing;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CreateOrderTests : AccessServiceTestsBase
{
    public CreateOrderTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task POST_orders_PublishedPlan_SalesClosedWithoutProviderCallOrNewRows(bool useIdempotencyKey)
    {
        Guid planId = await SeedPublicPlanAsync("closed-sales", 500_000);
        Guid userId = Guid.NewGuid();
        Order historicalOrder = Order.Create(userId, planId, 400_000, "RUB", "tbank").Value;
        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(historicalOrder);
            db.BillingConfigs.Add(BillingConfig.Create(true, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        });
        AuthenticateAs("platform-participant", userId);
        if (useIdempotencyKey)
            AppHttpClient.DefaultRequestHeaders.Add("Idempotency-Key", "old-session-key");

        for (int attempt = 0; attempt < 2; attempt++)
        {
            HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
                "/access/orders/", new CreateOrderRequest(planId));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Envelope? envelope = await response.Content.ReadFromJsonAsync<Envelope>();
            Assert.Contains(envelope!.Error!.Messages, error => error.Code == "billing.sales_closed");
        }
        Assert.Empty(Factory.TBankClient.InitCalls);
        Assert.Empty(Factory.TBankClient.ChargeCalls);
        await ExecuteInDbAsync(async db =>
        {
            Order persisted = Assert.Single(await db.Orders.AsNoTracking().ToListAsync());
            Assert.Equal(historicalOrder.Id, persisted.Id);
            Assert.Equal(400_000, persisted.AmountCents);
            Assert.Equal(OrderStatus.PENDING, persisted.Status);
            Assert.Empty(await db.IdempotencyKeys.ToListAsync());
            Assert.Empty(await db.PlanGrants.ToListAsync());
        });
    }

    [Fact]
    public async Task POST_orders_Anonymous_Returns401()
    {
        RemoveAuthentication();
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/access/orders/", new CreateOrderRequest(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(Factory.TBankClient.InitCalls);
    }

    [Fact]
    public async Task BillingConfig_AlwaysClosed_AdminCannotReopenOrRewriteHistoricalSetting()
    {
        DateTimeOffset originalTimestamp = DateTimeOffset.UtcNow.AddDays(-7);
        await ExecuteInDbAsync(async db =>
        {
            db.BillingConfigs.Add(BillingConfig.Create(true, originalTimestamp));
            await db.SaveChangesAsync();
        });
        RemoveAuthentication();
        BillingConfigDto? config = (await AppHttpClient.GetFromJsonAsync<Envelope<BillingConfigDto>>(
            "/access/billing-config/"))!.Result;
        Assert.False(config!.IsEnabled);
        AuthenticateAsAdmin();
        HttpResponseMessage enable = await AppHttpClient.PatchAsJsonAsync(
            "/access/billing-config/", new UpdateBillingConfigRequest(true));
        Assert.Equal(HttpStatusCode.Conflict, enable.StatusCode);
        HttpResponseMessage disable = await AppHttpClient.PatchAsJsonAsync(
            "/access/billing-config/", new UpdateBillingConfigRequest(false));
        disable.EnsureSuccessStatusCode();
        await ExecuteInDbAsync(async db =>
        {
            BillingConfig historical = await db.BillingConfigs.AsNoTracking().SingleAsync();
            Assert.True(historical.IsEnabled);
            Assert.Equal(originalTimestamp, historical.UpdatedAt, TimeSpan.FromMicroseconds(1));
        });
    }

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