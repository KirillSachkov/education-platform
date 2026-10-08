namespace NotificationService.Contracts.Preferences.Requests;

/// <summary>
/// Запрос на обновление per-user настроек каналов / Preferences update request.
///
/// <see cref="OptedOutTypes"/> — полный список типов, от которых пользователь отписан. Семантика —
/// replace (PUT): сервер заменит прежний набор этим. Пустой список = подписан на всё.
/// </summary>
public sealed record UpdatePreferencesRequest
{
    public required bool TelegramEnabled { get; init; }

    public required bool EmailEnabled { get; init; }

    /// <summary>
    /// Глобальный тумблер Web Push. Не-<c>required</c> для backward-compat (старый клиент
    /// не пришлёт → дефолт <c>true</c>, push остаётся включён). Новый клиент всегда шлёт реальное значение.
    /// </summary>
    public bool WebPushEnabled { get; init; } = true;

    /// <summary>
    /// Полный список типов, от которых пользователь отписан (<c>NotificationType</c> short-коды).
    /// Не-<c>required</c> для backward-compatibility: старый клиент не пришлёт поле → сервер не тронет opt-outs.
    /// NB: если PUT отправлен без <c>OptedOutTypes</c>, <c>UpdateMyPreferencesHandler</c> обработает это как
    /// «заменить пустым набором» — т.е. снимет все opt-out'ы. Клиент должен ВСЕГДА отдавать текущий полный набор.
    /// </summary>
    public IReadOnlyList<short> OptedOutTypes { get; init; } = [];
}
