using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Billing;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class ListMyOrdersTests : AccessServiceTestsBase
{
    public ListMyOrdersTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task GET_myOrders_ReturnsPlanTitle()
    {
        // #512 — название плана отдаётся прямо в заказе, фронту не нужен каталог планов.
        Guid userId = Guid.NewGuid();
        Plan plan = await SeedPlanAsync("Полный доступ");
        Order order = await SeedOrderAsync(userId, plan.Id, OrderStatus.PAID);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/me/orders/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<ListMyOrdersResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<ListMyOrdersResponse>>();
        Assert.NotNull(envelope);
        Assert.False(envelope!.IsError);
        ListMyOrdersResponse body = envelope.Result!;
        MeOrderSummary summary = Assert.Single(body.Items);
        Assert.Equal(order.Id, summary.OrderId);
        Assert.Equal(plan.Id, summary.PlanId);
        Assert.Equal("Полный доступ", summary.PlanTitle);
    }

    [Fact]
    public async Task GET_myOrders_PlanMissing_ReturnsNullPlanTitle()
    {
        // Заказ ссылается на несуществующий план (план удалён) — PlanTitle null,
        // а не 500 и не выпавший из списка заказ.
        Guid userId = Guid.NewGuid();
        Order order = await SeedLegacyOrphanOrderAsync(userId, Guid.NewGuid());
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/me/orders/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<ListMyOrdersResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<ListMyOrdersResponse>>();
        Assert.NotNull(envelope);
        ListMyOrdersResponse body = envelope!.Result!;
        MeOrderSummary summary = Assert.Single(body.Items);
        Assert.Equal(order.Id, summary.OrderId);
        Assert.Null(summary.PlanTitle);
    }

    [Fact]
    public async Task GET_myOrders_ReturnsOnlyOwnOrders()
    {
        Guid userId = Guid.NewGuid();
        Guid otherUserId = Guid.NewGuid();
        Plan plan = await SeedPlanAsync("Только мой план");
        Order myOrder = await SeedOrderAsync(userId, plan.Id, OrderStatus.PENDING);
        await SeedOrderAsync(otherUserId, plan.Id, OrderStatus.PAID);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/access/me/orders/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<ListMyOrdersResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<ListMyOrdersResponse>>();
        ListMyOrdersResponse body = envelope!.Result!;
        MeOrderSummary summary = Assert.Single(body.Items);
        Assert.Equal(myOrder.Id, summary.OrderId);
    }

    // ─── helpers ─────────────────────────────────────────────────────────

    private async Task<Plan> SeedPlanAsync(string displayName)
    {
        Plan plan = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.FULL_ALL,
            slug: PlanSlug.Of($"plan-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of(displayName).Value,
            courseIds: [], requestedCapabilities: null).Value;

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            await db.SaveChangesAsync();
        });
        return plan;
    }

    private async Task<Order> SeedOrderAsync(Guid userId, Guid planId, OrderStatus targetStatus)
    {
        Order order = Order.Create(
            userId, planId, amountCents: 500_000, currency: "RUB", provider: "tbank").Value;

        switch (targetStatus)
        {
            case OrderStatus.PENDING:
                break;
            case OrderStatus.PAID:
                order.MarkPaid("ext-pid-" + Guid.NewGuid().ToString("N")[..8]);
                break;
            case OrderStatus.FAILED:
                order.MarkFailed("rejected");
                break;
            case OrderStatus.REFUNDED:
                order.MarkPaid("ext-pid-" + Guid.NewGuid().ToString("N")[..8]);
                order.Refund("refund");
                break;
        }

        await ExecuteInDbAsync(async db =>
        {
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });
        return order;
    }

    private async Task<Order> SeedLegacyOrphanOrderAsync(Guid userId, Guid planId)
    {
        Order order = Order.Create(
            userId, planId, amountCents: 500_000, currency: "RUB", provider: "tbank").Value;
        order.MarkFailed("rejected");

        await ExecuteInDbAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });
        return order;
    }
}
