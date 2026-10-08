using System.Security.Cryptography;
using System.Text;
using AccessService.Core.Features.Billing.TBank;

namespace AccessService.IntegrationTests.Features.Billing;

public class TBankSignatureTests
{
    private const string PASSWORD = "test-password";

    [Fact]
    public void ComputeToken_KnownVector_FromTBankDocs()
    {
        // Vector adapted from T-Bank docs canonical example.
        // Fields: Amount=19200, OrderId=21090, Password=12345, TerminalKey=MerchantTerminalKey
        // Sorted Ordinal by key → Amount, OrderId, Password, TerminalKey
        // Concat values: "19200" + "21090" + "12345" + "MerchantTerminalKey"
        // = "192002109012345MerchantTerminalKey"
        var fields = new Dictionary<string, string>
        {
            ["Amount"] = "19200",
            ["OrderId"] = "21090",
            ["TerminalKey"] = "MerchantTerminalKey",
        };

        string token = TBankSignature.ComputeToken(fields, "12345");

        // Pre-computed sha256 of "192002109012345MerchantTerminalKey":
        Assert.Equal("9e7bc1f7ead4f1245c5613953a79cae8e8734f8a46e9547a9bbbe35fb8aed14b", token);
    }

    [Fact]
    public void ComputeToken_BoolValuesNormalizedToLowercase()
    {
        var fields = new Dictionary<string, string>
        {
            ["Success"] = "true",
            ["Status"] = "CONFIRMED",
        };

        string token = TBankSignature.ComputeToken(fields, PASSWORD);

        // sorted Ordinal: Password, Status, Success → "test-password" + "CONFIRMED" + "true"
        // = "test-passwordCONFIRMEDtrue"
        string expected = ComputeExpectedHash("test-passwordCONFIRMEDtrue");
        Assert.Equal(expected, token);
    }

    [Fact]
    public void VerifyToken_ValidToken_ReturnsTrue()
    {
        var fields = new Dictionary<string, string>
        {
            ["Amount"] = "500000",
            ["OrderId"] = "abc",
            ["TerminalKey"] = "key",
        };
        string token = TBankSignature.ComputeToken(fields, PASSWORD);

        bool result = TBankSignature.VerifyToken(fields, PASSWORD, token);

        Assert.True(result);
    }

    [Fact]
    public void VerifyToken_TamperedField_ReturnsFalse()
    {
        var fields = new Dictionary<string, string>
        {
            ["Amount"] = "500000",
            ["OrderId"] = "abc",
            ["TerminalKey"] = "key",
        };
        string token = TBankSignature.ComputeToken(fields, PASSWORD);

        fields["Amount"] = "1";

        bool result = TBankSignature.VerifyToken(fields, PASSWORD, token);

        Assert.False(result);
    }

    [Fact]
    public void VerifyToken_WrongPassword_ReturnsFalse()
    {
        var fields = new Dictionary<string, string>
        {
            ["Amount"] = "500000",
            ["OrderId"] = "abc",
        };
        string token = TBankSignature.ComputeToken(fields, PASSWORD);

        bool result = TBankSignature.VerifyToken(fields, "wrong-password", token);

        Assert.False(result);
    }

    [Fact]
    public void VerifyToken_DifferentCase_ReturnsFalse()
    {
        // sha256 hex always lowercase. Case-sensitive compare → uppercase token must fail.
        var fields = new Dictionary<string, string>
        {
            ["Amount"] = "500000",
            ["OrderId"] = "abc",
        };
        string token = TBankSignature.ComputeToken(fields, PASSWORD);
        string upperToken = token.ToUpperInvariant();

        bool result = TBankSignature.VerifyToken(fields, PASSWORD, upperToken);

        Assert.False(result);
    }

    private static string ComputeExpectedHash(string input)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
