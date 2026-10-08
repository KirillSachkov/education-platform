namespace NotificationService.Contracts.Preferences.Dtos;

/// <summary>
/// Per-user настройки каналов доставки / Per-user delivery channel preferences.
/// <see cref="TelegramEnabled"/> требует привязки Telegram (иначе фронт прячет toggle).
/// InApp всегда включён — флага нет, это продуктовое решение.
///
/// <see cref="OptedOutTypes"/> — список <c>NotificationType</c> (<c>short</c>-коды), от которых
/// пользователь отписался per-type. Ортогонально каналам: отписка скрывает уведомление во ВСЕХ
/// каналах одновременно. Пустой список = пользователь подписан на всё (дефолт).
/// </summary>
public sealed record NotificationPreferenceDto
{
    /// <summary>Включены ли Telegram-уведомления / Whether Telegram notifications are enabled.</summary>
    public required bool TelegramEnabled { get; init; }

    /// <summary>Включены ли email-уведомления / Whether email notifications are enabled.</summary>
    public required bool EmailEnabled { get; init; }

    /// <summary>
    /// Включены ли Web Push уведомления / Whether Web Push notifications are enabled.
    /// Глобальный тумблер устройства-агностичен; сама подписка устройства живёт отдельно
    /// (<c>POST /notifications/push/subscriptions/</c>). Не-<c>required</c> для backward-compat.
    /// </summary>
    public bool WebPushEnabled { get; init; } = true;

    /// <summary>
    /// Типы (<see cref="short"/>-коды), от которых пользователь отписался per-type.
    /// Не-<c>required</c> для backward-compatibility при rolling deploy (старый клиент может не присылать поле).
    /// </summary>
    public IReadOnlyList<short> OptedOutTypes { get; init; } = [];
}
