using System.Security.Cryptography;
using System.Text;
using Shared.GitHubApp;

namespace AccessService.IntegrationTests.Features.Integrations.GitHubApp;

/// <summary>
///     Unit tests на security-critical HMAC-SHA256 verification GitHub webhook'ов.
///     Не требует Testcontainers / DB — чистые unit'ы. Кладём в integration-проект
///     для удобства запуска одной командой <c>dotnet test</c>.
/// </summary>
public sealed class WebhookSignatureVerifierTests
{
    private const string SECRET = "test-secret-do-not-use-in-prod";

    [Fact]
    public void Verify_valid_signature_returns_true()
    {
        byte[] body = "{\"action\":\"member_added\"}"u8.ToArray();
        string signature = Sign(body, SECRET);

        bool result = WebhookSignatureVerifier.Verify(signature, body, SECRET);

        Assert.True(result);
    }

    [Fact]
    public void Verify_modified_body_returns_false()
    {
        byte[] body = "{\"action\":\"member_added\"}"u8.ToArray();
        byte[] tampered = "{\"action\":\"member_removed\"}"u8.ToArray();
        string signature = Sign(body, SECRET);

        bool result = WebhookSignatureVerifier.Verify(signature, tampered, SECRET);

        Assert.False(result);
    }

    [Fact]
    public void Verify_wrong_secret_returns_false()
    {
        byte[] body = "{\"action\":\"member_added\"}"u8.ToArray();
        string signature = Sign(body, "different-secret");

        bool result = WebhookSignatureVerifier.Verify(signature, body, SECRET);

        Assert.False(result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing-prefix")]
    [InlineData("sha256=")]
    [InlineData("sha256=invalid-hex-zzz")]
    public void Verify_malformed_header_returns_false(string? signature)
    {
        byte[] body = "{\"action\":\"member_added\"}"u8.ToArray();

        bool result = WebhookSignatureVerifier.Verify(signature, body, SECRET);

        Assert.False(result);
    }

    [Fact]
    public void Verify_empty_body_succeeds_with_correct_signature()
    {
        byte[] body = [];
        string signature = Sign(body, SECRET);

        bool result = WebhookSignatureVerifier.Verify(signature, body, SECRET);

        Assert.True(result);
    }

    [Fact]
    public void Verify_constant_time_compare_resists_partial_match()
    {
        // Сигнатура отличается только последним байтом. Если бы compare не был
        // constant-time, мы бы могли использовать этот тест для timing-атаки.
        // Здесь проверяем только корректность результата (false).
        byte[] body = "test"u8.ToArray();
        string correctSignature = Sign(body, SECRET);
        // Последний символ заменили
        string mutated = correctSignature[..^1] + (correctSignature[^1] == 'a' ? 'b' : 'a');

        bool result = WebhookSignatureVerifier.Verify(mutated, body, SECRET);

        Assert.False(result);
    }

    private static string Sign(byte[] body, string secret)
    {
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(body);
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
