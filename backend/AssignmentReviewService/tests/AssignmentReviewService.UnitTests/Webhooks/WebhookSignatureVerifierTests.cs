using System.Security.Cryptography;
using System.Text;
using Shared.GitHubApp;

namespace AssignmentReviewService.UnitTests.Webhooks;

public sealed class WebhookSignatureVerifierTests
{
    private const string SECRET = "my-webhook-secret-32-bytes-long!!";

    private static string ComputeExpected(byte[] body, string secret)
    {
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    [Fact]
    public void Verify_ValidSignature_ReturnsTrue()
    {
        byte[] body = Encoding.UTF8.GetBytes("""{"action":"created"}""");
        string signature = ComputeExpected(body, SECRET);

        Assert.True(WebhookSignatureVerifier.Verify(signature, body, SECRET));
    }

    [Fact]
    public void Verify_NullSignature_ReturnsFalse()
    {
        Assert.False(WebhookSignatureVerifier.Verify(null, Array.Empty<byte>(), SECRET));
    }

    [Fact]
    public void Verify_EmptySignature_ReturnsFalse()
    {
        Assert.False(WebhookSignatureVerifier.Verify(string.Empty, Array.Empty<byte>(), SECRET));
    }

    [Fact]
    public void Verify_SignatureWithoutSha256Prefix_ReturnsFalse()
    {
        byte[] body = Encoding.UTF8.GetBytes("body");
        string raw = ComputeExpected(body, SECRET).Substring("sha256=".Length);

        Assert.False(WebhookSignatureVerifier.Verify(raw, body, SECRET));
    }

    [Fact]
    public void Verify_WrongPrefix_ReturnsFalse()
    {
        Assert.False(WebhookSignatureVerifier.Verify("sha1=abc", Array.Empty<byte>(), SECRET));
    }

    [Fact]
    public void Verify_NonHexCharacters_ReturnsFalse()
    {
        Assert.False(WebhookSignatureVerifier.Verify("sha256=zzznotvalidhex", Array.Empty<byte>(), SECRET));
    }

    [Fact]
    public void Verify_CorrectPrefix_WrongHash_ReturnsFalse()
    {
        byte[] body = Encoding.UTF8.GetBytes("body");
        string wrong = "sha256=" + new string('a', 64);

        Assert.False(WebhookSignatureVerifier.Verify(wrong, body, SECRET));
    }

    [Fact]
    public void Verify_DifferentSecret_ReturnsFalse()
    {
        byte[] body = Encoding.UTF8.GetBytes("""{"action":"created"}""");
        string signature = ComputeExpected(body, SECRET);

        Assert.False(WebhookSignatureVerifier.Verify(signature, body, "different-secret"));
    }

    [Fact]
    public void Verify_DifferentBody_ReturnsFalse()
    {
        byte[] originalBody = Encoding.UTF8.GetBytes("""{"action":"created"}""");
        byte[] tamperedBody = Encoding.UTF8.GetBytes("""{"action":"deleted"}""");
        string signature = ComputeExpected(originalBody, SECRET);

        Assert.False(WebhookSignatureVerifier.Verify(signature, tamperedBody, SECRET));
    }

    [Fact]
    public void Verify_EmptyBody_CorrectSignature_ReturnsTrue()
    {
        byte[] empty = Array.Empty<byte>();
        string signature = ComputeExpected(empty, SECRET);

        Assert.True(WebhookSignatureVerifier.Verify(signature, empty, SECRET));
    }

    [Fact]
    public void Verify_UpperCaseHex_StillReturnsTrue()
    {
        byte[] body = Encoding.UTF8.GetBytes("body");
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(SECRET));
        string upperHex = "sha256=" + Convert.ToHexString(hmac.ComputeHash(body));

        // Convert.FromHexString accepts both cases; verifier should too.
        Assert.True(WebhookSignatureVerifier.Verify(upperHex, body, SECRET));
    }

    [Fact]
    public void PreviewSignature_LongString_TruncatesWithEllipsis()
    {
        const string sig = "sha256=" + "abcdef0123456789";
        string preview = WebhookSignatureVerifier.PreviewSignature(sig);

        Assert.EndsWith("…", preview, StringComparison.Ordinal);
        Assert.True(preview.Length <= 21);
    }

    [Fact]
    public void PreviewSignature_Empty_ReturnsPlaceholder()
    {
        Assert.Equal("(empty)", WebhookSignatureVerifier.PreviewSignature(null));
        Assert.Equal("(empty)", WebhookSignatureVerifier.PreviewSignature(string.Empty));
    }
}
