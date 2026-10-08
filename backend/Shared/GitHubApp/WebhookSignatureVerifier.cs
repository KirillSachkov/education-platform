using System.Security.Cryptography;
using System.Text;

namespace Shared.GitHubApp;

/// <summary>
///     Verify HMAC-SHA256 подписи входящего webhook.
///     GitHub шлёт хедер <c>X-Hub-Signature-256: sha256={hex}</c>.
///
///     Constant-time compare через <see cref="CryptographicOperations.FixedTimeEquals"/>.
///     Прежде жил в каждом сервисе отдельно — extract в Shared (#296).
/// </summary>
public static class WebhookSignatureVerifier
{
    private const string PREFIX = "sha256=";

    /// <summary>
    ///     Возвращает <c>true</c> если подпись совпадает.
    ///     Если <paramref name="signatureHeader"/> пустой/null — false.
    /// </summary>
    public static bool Verify(string? signatureHeader, byte[] body, string secret)
    {
        if (string.IsNullOrEmpty(signatureHeader)
            || !signatureHeader.StartsWith(PREFIX, StringComparison.Ordinal))
        {
            return false;
        }

        string providedHex = signatureHeader[PREFIX.Length..];
        byte[] providedBytes;
        try
        {
            providedBytes = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        byte[] computed = hmac.ComputeHash(body);

        return CryptographicOperations.FixedTimeEquals(providedBytes, computed);
    }

    /// <summary>Helper для логирования: prefix-only сравнения не делаем, только debug.</summary>
    public static string PreviewSignature(string? signatureHeader)
    {
        if (string.IsNullOrEmpty(signatureHeader)) return "(empty)";
        return signatureHeader.Length > 20 ? signatureHeader[..20] + "…" : signatureHeader;
    }
}
