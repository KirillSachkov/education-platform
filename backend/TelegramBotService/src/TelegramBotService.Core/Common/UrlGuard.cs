namespace TelegramBotService.Core.Common;

/// <summary>
///     Telegram Bot API requires publicly resolvable HTTP URLs in inline keyboard buttons.
///     Internal hostnames (<c>localhost</c>, <c>nginx</c>, <c>127.*</c>, <c>10.*</c>, <c>192.168.*</c>,
///     <c>172.*</c>) get rejected with 400 «Wrong HTTP URL». In dev environment we just don't
///     render URL buttons — user has nothing to tap on their phone anyway.
///
///     В prod (https://your-domain.app) — `IsPublic` возвращает true и кнопки рендерятся.
/// </summary>
public static class UrlGuard
{
    public static bool IsPublic(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        string host = uri.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "nginx", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("127.", StringComparison.Ordinal)
            || host.StartsWith("10.", StringComparison.Ordinal)
            || host.StartsWith("192.168.", StringComparison.Ordinal)
            || host.StartsWith("172.", StringComparison.Ordinal))
        {
            return false;
        }

        return host.Contains('.', StringComparison.Ordinal);
    }
}
