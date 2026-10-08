namespace AuthService.Contracts;

/// <summary>
///     Ответ на <c>POST /auth/telegram/verify</c>. Позволяет Telegram-боту показать
///     приветственное сообщение с username платформы.
/// </summary>
public sealed record VerifyTelegramLinkResponse(
    Guid UserId,
    string Username);
