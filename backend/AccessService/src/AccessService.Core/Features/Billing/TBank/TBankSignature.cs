using System.Security.Cryptography;
using System.Text;

namespace AccessService.Core.Features.Billing.TBank;

/// <summary>
/// T-Bank эквайринг token (signature) — sha256 hex.
///
/// Алгоритм (см. https://developer.tbank.ru/eacq/api):
/// 1. Берём верхнеуровневые поля. Receipt и DATA НЕ участвуют в Init token;
///    в Notification — все поля кроме Token.
/// 2. Добавляем пару (Password, password_value).
/// 3. Sort by key (Ordinal).
/// 4. Concat values без разделителей.
/// 5. SHA256 → hex lowercase.
///
/// Caller отвечает за приведение значений к корректным строкам (bool → "true"/"false"
/// lowercase, числа → InvariantCulture). Помещение/исключение полей (Receipt/DATA) —
/// тоже на caller'е.
/// </summary>
public static class TBankSignature
{
    /// <summary>
    /// Считает token по алгоритму T-Bank: sort Ordinal по ключам (включая Password) →
    /// concat values → SHA256 → hex lowercase.
    /// </summary>
    public static string ComputeToken(IReadOnlyDictionary<string, string> fields, string password)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in fields)
            sorted[kv.Key] = kv.Value ?? string.Empty;
        sorted["Password"] = password;

        var sb = new StringBuilder();
        foreach (var kv in sorted)
            sb.Append(kv.Value);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Проверяет совпадение <paramref name="providedToken"/> с пересчитанным token'ом.
    /// Сравнение через <see cref="CryptographicOperations.FixedTimeEquals"/> для устойчивости
    /// к timing-атакам.
    /// </summary>
    public static bool VerifyToken(
        IReadOnlyDictionary<string, string> fields,
        string password,
        string providedToken)
    {
        if (string.IsNullOrEmpty(providedToken))
            return false;

        string expected = ComputeToken(fields, password);

        if (expected.Length != providedToken.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected),
            Encoding.ASCII.GetBytes(providedToken));
    }
}
