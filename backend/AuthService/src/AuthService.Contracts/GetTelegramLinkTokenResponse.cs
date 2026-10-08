namespace AuthService.Contracts;

/// <summary>
///     Ответ на <c>GET /users/me/telegram/link-token</c>.
/// </summary>
/// <param name="LinkToken">Одноразовый URL-safe токен, TTL 10 минут.</param>
/// <param name="BotUsername">Username Telegram-бота без <c>@</c>.</param>
/// <param name="DeepLinkUrl">Готовая ссылка вида <c>https://t.me/{BotUsername}?start={LinkToken}</c>.</param>
public sealed record GetTelegramLinkTokenResponse(
    string LinkToken,
    string BotUsername,
    string DeepLinkUrl);
