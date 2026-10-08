using AccessService.Domain;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.IntegrationTests.Features.Billing;

public class OrderAttachExternalRefTests
{
    [Fact]
    public void AttachExternalRef_WhenRefIsNull_SetsRef()
    {
        Result<Order, Error> orderResult = Order.Create(
            userId: Guid.NewGuid(),
            planId: Guid.NewGuid(),
            amountCents: 500000,
            currency: "RUB",
            provider: "tbank");
        Order order = orderResult.Value;

        UnitResult<Error> result = order.AttachExternalRef("12345");

        Assert.True(result.IsSuccess);
        Assert.Equal("12345", order.ExternalProviderRef);
        Assert.Equal(OrderStatus.PENDING, order.Status);
    }

    [Fact]
    public void AttachExternalRef_WhenSameRef_IsIdempotent()
    {
        Order order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), 500000, "RUB", "tbank").Value;
        order.AttachExternalRef("12345");

        UnitResult<Error> result = order.AttachExternalRef("12345");

        Assert.True(result.IsSuccess);
        Assert.Equal("12345", order.ExternalProviderRef);
    }

    [Fact]
    public void AttachExternalRef_WhenDifferentRef_ReturnsError()
    {
        Order order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), 500000, "RUB", "tbank").Value;
        order.AttachExternalRef("12345");

        UnitResult<Error> result = order.AttachExternalRef("99999");

        Assert.True(result.IsFailure);
        Assert.Equal("order.external_ref.conflict", result.Error.Messages[0].Code);
    }

    [Fact]
    public void AttachExternalRef_WhenEmptyRef_ReturnsError()
    {
        Order order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), 500000, "RUB", "tbank").Value;

        UnitResult<Error> result = order.AttachExternalRef("");

        Assert.True(result.IsFailure);
        Assert.Equal("order.external_ref.empty", result.Error.Messages[0].Code);
    }

    [Fact]
    public void AttachExternalRef_WhenTooLong_ReturnsError()
    {
        Order order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), 500000, "RUB", "tbank").Value;

        UnitResult<Error> result = order.AttachExternalRef(new string('a', Order.EXTERNAL_REF_MAX_LENGTH + 1));

        Assert.True(result.IsFailure);
        Assert.Equal("order.external_ref.too_long", result.Error.Messages[0].Code);
    }
}
