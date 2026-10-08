using System.Security.Cryptography;
using System.Text;

namespace NotificationService.Core.Channels.Email;

/// <summary>
/// Сводит email к каноническому «физическому инбоксу» для дедупа доставки глобального дайджеста.
///
/// <para>
/// Проблема: <c>RequireUniqueEmail</c> в Identity сравнивает лишь нормализованный (UPPER-case) адрес,
/// поэтому Gmail-алиасы (<c>kir@</c>, <c>k.ir@</c>, <c>kir+promo@</c>) — это РАЗНЫЕ аккаунты, но один
/// физический инбокс. Дайджест шлёт по письму на каждый аккаунт → один ящик получает дубль.
/// </para>
///
/// <para>
/// Правила Gmail/Googlemail: точки в local-part игнорируются, <c>+tag</c> (subaddressing) отбрасывается,
/// <c>googlemail.com</c> эквивалентен <c>gmail.com</c>. Для остальных провайдеров канонизация
/// <b>консервативная</b> (только trim + lower-case): dot-/plus-семантика не универсальна, и сворачивать
/// её рискованно — можно слить разные ящики.
/// </para>
/// </summary>
public static class EmailInboxCanonicalizer
{
    public static string Canonicalize(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        string normalized = email.Trim().ToLowerInvariant();

        int at = normalized.LastIndexOf('@');
        if (at <= 0 || at == normalized.Length - 1)
            return normalized; // нет валидного local@domain — отдаём как есть, не падаем

        string local = normalized[..at];
        string domain = normalized[(at + 1)..];

        if (domain is "gmail.com" or "googlemail.com")
        {
            domain = "gmail.com";

            int plus = local.IndexOf('+', StringComparison.Ordinal);
            if (plus >= 0)
                local = local[..plus];

            local = local.Replace(".", string.Empty, StringComparison.Ordinal);

            if (local.Length == 0)
                return normalized; // вырожденный адрес (напр. "+tag@gmail.com") — не сворачиваем
        }

        return $"{local}@{domain}";
    }

    /// <summary>
    /// SHA-256 канонического инбокса — ключ дедупа. Храним хеш, не сам адрес: PII в схему
    /// <c>notifications</c> не утекает (см. <c>GetAllUserIds</c> — «только id»).
    /// </summary>
    public static byte[] InboxHash(string email) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(email)));
}
