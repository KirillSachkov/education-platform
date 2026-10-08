namespace TelegramBotService.Contracts.Dtos;

/// <summary>
///     Admin/support-view привязки Telegram для произвольного пользователя
///     (<c>GET /telegram/admin/users/{userId}/link/</c>). Резолвится по <c>UserLink</c>
///     (PlatformUserId). Нет привязки → <see cref="Linked"/>=false, остальные поля null.
/// </summary>
public sealed record AdminTelegramLinkDto(
    bool Linked,
    long? TelegramUserId,
    string? TelegramUsername,
    DateTime? LinkedAt);
