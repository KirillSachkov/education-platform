namespace AuthService.Contracts;

public sealed record AuthUserLookupDto(
    Guid UserId,
    string? Name,
    string? Username,
    string Email,
    Guid? AvatarId,
    // Telegram @handle (из provider_display_name), пусто если Telegram не привязан. Заполняет
    // только batch-lookup для review-обогащения и author-help уведомлений. #575.
    string? TelegramUsername = null);
