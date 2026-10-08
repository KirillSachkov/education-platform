using AccessService.Core.Features.Billing.TBank;

namespace AccessService.IntegrationTests.Features.Billing;

public class TBankStatusMapperTests
{
    [Theory]
    [InlineData("AUTHORIZED", "AUTHORIZED", null)]
    [InlineData("CONFIRMED", "PAID", null)]
    [InlineData("REJECTED", "FAILED", "rejected")]
    [InlineData("REVERSED", "FAILED", "reversed")]
    [InlineData("DEADLINE_EXPIRED", "FAILED", "deadline_expired")]
    [InlineData("ATTEMPTS_EXPIRED", "FAILED", "attempts_expired")]
    [InlineData("CANCELED", "FAILED", "canceled")]
    [InlineData("REFUNDED", "REFUNDED", "provider refund")]
    public void Map_ProducesCorrectMapping(string tbankStatus, string expectedStatus, string? expectedReasonContains)
    {
        TBankStatusMapping result = TBankStatusMapper.Map(tbankStatus, errorCode: "0");

        Assert.Equal(expectedStatus, result.NormalizedStatus);
        if (expectedReasonContains is not null)
            Assert.Contains(expectedReasonContains, result.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NEW")]
    [InlineData("FORM_SHOWED")]
    [InlineData("AUTHORIZING")]
    [InlineData("CONFIRMING")]
    public void Map_IntermediateStatus_ReturnsNoOp(string tbankStatus)
    {
        TBankStatusMapping result = TBankStatusMapper.Map(tbankStatus, errorCode: "0");

        Assert.Equal("NOOP", result.NormalizedStatus);
    }

    [Fact]
    public void Map_RejectedIncludesErrorCode()
    {
        TBankStatusMapping result = TBankStatusMapper.Map("REJECTED", errorCode: "1051");

        Assert.Equal("FAILED", result.NormalizedStatus);
        Assert.Contains("1051", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_PartialRefunded_TreatedAsNoOpWithLog()
    {
        TBankStatusMapping result = TBankStatusMapper.Map("PARTIAL_REFUNDED", errorCode: "0");

        Assert.Equal("NOOP", result.NormalizedStatus);
        Assert.Equal("partial_refunded_unsupported", result.Reason);
    }

    [Fact]
    public void Map_UnknownStatus_ThrowsArgumentException()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() =>
            TBankStatusMapper.Map("FROBNICATED_STATUS_999", errorCode: "0"));

        Assert.Contains("FROBNICATED_STATUS_999", ex.Message, StringComparison.Ordinal);
    }
}
