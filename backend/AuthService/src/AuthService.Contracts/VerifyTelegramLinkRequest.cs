namespace AuthService.Contracts;

/// <summary>
///     Запрос service-to-service на <c>POST /auth/telegram/verify</c> от TelegramBotService.
/// </summary>
/// <param name="LinkToken">Токен, полученный пользователем с сайта и переданный в <c>/start &lt;token&gt;</c>.</param>
/// <param name="TelegramUserId">Telegram numeric user id (= chat id в личке).</param>
/// <param name="TelegramUsername">Необязательный <c>@username</c> — сохраняется как display-значение.</param>
public sealed record VerifyTelegramLinkRequest(
    string LinkToken,
    long TelegramUserId,
    string? TelegramUsername);
