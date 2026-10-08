using AccessService.Domain;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

/// <summary>
/// Pure domain unit-tests на state-machine <see cref="Order"/> (#414): идемпотентность
/// терминальных переходов (MarkPaid/MarkFailed) и валидные/невалидные переходы.
/// </summary>
public class OrderStateMachineTests
{
    private static Order NewPending() =>
        Order.Create(Guid.NewGuid(), Guid.NewGuid(), 500_000, "RUB", "tbank").Value;

    [Fact]
    public void MarkFailed_WhenAlreadyFailed_IsIdempotent()
    {
        Order order = NewPending();
        UnitResult<Error> first = order.MarkFailed("rejected");
        Assert.True(first.IsSuccess);
        Assert.Equal(OrderStatus.FAILED, order.Status);

        UnitResult<Error> second = order.MarkFailed("another reason");

        Assert.True(second.IsSuccess);
        Assert.Equal(OrderStatus.FAILED, order.Status);
        // FailureReason не перезаписывается повторным вызовом.
        Assert.Equal("rejected", order.FailureReason);
    }

    [Fact]
    public void MarkFailed_WhenPaid_ReturnsNotPending()
    {
        Order order = NewPending();
        order.MarkPaid("ext-1");

        UnitResult<Error> result = order.MarkFailed("late reject");

        Assert.True(result.IsFailure);
        Assert.Equal("order.status.not_pending", result.Error.Messages[0].Code);
        Assert.Equal(OrderStatus.PAID, order.Status);
    }

    [Fact]
    public void MarkPaid_WhenAlreadyPaid_IsIdempotent()
    {
        Order order = NewPending();
        order.MarkPaid("ext-1");

        UnitResult<Error> second = order.MarkPaid("ext-1");

        Assert.True(second.IsSuccess);
        Assert.Equal(OrderStatus.PAID, order.Status);
    }

    [Fact]
    public void MarkPaid_WhenFailed_ReturnsNotPending()
    {
        Order order = NewPending();
        order.MarkFailed("rejected");

        UnitResult<Error> result = order.MarkPaid("ext-1");

        Assert.True(result.IsFailure);
        Assert.Equal("order.status.not_pending", result.Error.Messages[0].Code);
        Assert.Equal(OrderStatus.FAILED, order.Status);
    }

    [Fact]
    public void Refund_WhenPending_ConvergesToProviderRefundedState()
    {
        Order order = NewPending();

        UnitResult<Error> result = order.Refund("chargeback");

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.REFUNDED, order.Status);
    }

    [Fact]
    public void Refund_WhenFailed_ReturnsNotPaid()
    {
        Order order = NewPending();
        order.MarkFailed("rejected");

        UnitResult<Error> result = order.Refund("chargeback");

        Assert.True(result.IsFailure);
        Assert.Equal("order.status.not_paid", result.Error.Messages[0].Code);
    }

    // ---- Recurring renewal order (#614) ----

    [Fact]
    public void Create_DefaultsTo_InitialChargeType_WithNoRebillId()
    {
        Order order = NewPending();

        Assert.Equal(OrderChargeType.INITIAL, order.ChargeType);
        Assert.Null(order.RebillId);
    }

    [Fact]
    public void CreateRenewal_SetsRenewalChargeType_AndRebillId()
    {
        Guid grantId = Guid.NewGuid();
        Result<Order, Error> result = Order.CreateRenewal(
            Guid.NewGuid(), Guid.NewGuid(), grantId, 500_000, "RUB", "rb-1", "tbank");

        Assert.True(result.IsSuccess);
        Order order = result.Value;
        Assert.Equal(OrderChargeType.RENEWAL, order.ChargeType);
        Assert.Equal("rb-1", order.RebillId);
        Assert.Equal(grantId, order.RenewalGrantId);
        Assert.Equal(OrderStatus.PENDING, order.Status);
    }

    [Fact]
    public void CreateRenewal_WithoutRebillId_Fails()
    {
        Result<Order, Error> result = Order.CreateRenewal(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 500_000, "RUB", rebillId: "  ", provider: "tbank");

        Assert.True(result.IsFailure);
        Assert.Equal("order.rebill_id.empty", result.Error.Messages[0].Code);
    }

    [Fact]
    public void CreateRenewal_WithNonPositiveAmount_Fails()
    {
        Result<Order, Error> result = Order.CreateRenewal(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, "RUB", "rb-1");

        Assert.True(result.IsFailure);
        Assert.Equal("order.amount.invalid", result.Error.Messages[0].Code);
    }
}
