using System.Net;
using System.Net.Http.Json;
using AccessService.Contracts.Billing;
using AccessService.Domain;
using AccessService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetOrderStatusTests : AccessServiceTestsBase
{
    public GetOrderStatusTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task GET_status_OwnerPending_Returns200AndStatus()
    {
        Guid userId = Guid.NewGuid();
        Order order = await SeedOrderAsync(userId, OrderStatus.PENDING);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/orders/{order.Id}/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<GetOrderStatusResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetOrderStatusResponse>>();
        Assert.NotNull(envelope);
        Assert.False(envelope!.IsError);
        GetOrderStatusResponse body = envelope.Result!;
        Assert.Equal(order.Id, body.OrderId);
        Assert.Equal("PENDING", body.Status);
        Assert.Null(body.PaidAt);
        Assert.Null(body.FailureReason);
    }

    [Fact]
    public async Task GET_status_OwnerPaid_ReturnsPaidAt()
    {
        Guid userId = Guid.NewGuid();
        Order order = await SeedOrderAsync(userId, OrderStatus.PAID);
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/orders/{order.Id}/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<GetOrderStatusResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetOrderStatusResponse>>();
        Assert.NotNull(envelope);
        GetOrderStatusResponse body = envelope!.Result!;
        Assert.Equal("PAID", body.Status);
        Assert.NotNull(body.PaidAt);
    }

    [Fact]
    public async Task GET_status_OwnerFailed_ReturnsUserFacingReason_NotRawInternal()
    {
        // #414: сырой внутренний FailureReason (errorCode'ы, суммы вроде
        // "amount_mismatch: webhook=..., order=...") НЕ должен утекать наружу — отдаём
        // нормализованное русское сообщение.
        Guid userId = Guid.NewGuid();
        Order order = await SeedOrderAsync(
            userId, OrderStatus.FAILED,
            failureReason: "amount_mismatch: webhook=100, order=500000");
        AuthenticateAs("platform-participant", userId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/orders/{order.Id}/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<GetOrderStatusResponse>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<GetOrderStatusResponse>>();
        Assert.NotNull(envelope);
        GetOrderStatusResponse body = envelope!.Result!;
        Assert.Equal("FAILED", body.Status);
        Assert.Equal("Сумма платежа не совпала с заказом", body.FailureReason);
        // Внутренние суммы не утекли.
        Assert.DoesNotContain("500000", body.FailureReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GET_status_DifferentUser_Returns403()
    {
        Guid ownerId = Guid.NewGuid();
        Order order = await SeedOrderAsync(ownerId, OrderStatus.PENDING);

        Guid otherId = Guid.NewGuid();
        AuthenticateAs("platform-participant", otherId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/orders/{order.Id}/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GET_status_AdminAnyOrder_Returns200()
    {
        Guid ownerId = Guid.NewGuid();
        Order order = await SeedOrderAsync(ownerId, OrderStatus.PENDING);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/orders/{order.Id}/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GET_status_NotFound_Returns404()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/orders/{Guid.NewGuid()}/status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GET_status_Anonymous_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/access/orders/{Guid.NewGuid()}/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Seeds an Order directly via DbContext in the target lifecycle state.
    /// Avoids POST /access/orders/ to keep tests focused on read-side behaviour.
    /// </summary>
    private async Task<Order> SeedOrderAsync(
        Guid userId,
        OrderStatus targetStatus,
        string? failureReason = null)
    {
        Plan plan = Plan.Create(
            authorId: Guid.NewGuid(),
            tier: PlanTier.FULL_ALL,
            slug: PlanSlug.Of($"status-{Guid.NewGuid():N}").Value,
            displayName: PlanDisplayName.Of("Status test plan").Value,
            courseIds: [],
            requestedCapabilities: null).Value;
        Order order = Order.Create(userId, plan.Id, amountCents: 500_000, currency: "RUB", provider: "tbank").Value;

        switch (targetStatus)
        {
            case OrderStatus.PENDING:
                break;
            case OrderStatus.PAID:
                order.MarkPaid("9999999999");
                break;
            case OrderStatus.FAILED:
                order.MarkFailed(failureReason);
                break;
            case OrderStatus.REFUNDED:
                order.MarkPaid("9999999999");
                order.Refund(failureReason);
                break;
        }

        await ExecuteInDbAsync(async db =>
        {
            db.Plans.Add(plan);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });
        return order;
    }
}
