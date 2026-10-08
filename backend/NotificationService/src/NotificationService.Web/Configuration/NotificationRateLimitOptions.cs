namespace NotificationService.Web.Configuration;

/// <summary>
/// Rate-limit настройки для чувствительных endpoint'ов NotificationService.
///
/// Биндятся из секции <c>NotificationRateLimit</c>. Партиция — по <c>sub</c> claim
/// (anonymous → по IP).
///
/// Политики:
/// <list type="bullet">
///   <item><see cref="StreamPermitLimit"/> — макс. активных SSE-соединений на user'а (default 3).
///     Защита от leak и от абуза (браузер-скрипт открывает десятки соединений).</item>
///   <item><see cref="PreferencesUpdatePermitLimit"/> + <see cref="PreferencesUpdateWindowMinutes"/>
///     — PUT /preferences/ (default 10/min).</item>
///   <item><see cref="BroadcastPermitLimit"/> + <see cref="BroadcastWindowMinutes"/>
///     — POST /broadcast/ (default 5/60min). Броадкаст — тяжёлая операция
///     (fan-out на N подписчиков), ограничиваем агрессивно.</item>
/// </list>
/// </summary>
public sealed class NotificationRateLimitOptions
{
    public const string SECTION_NAME = "NotificationRateLimit";

    /// <summary>Policy names — см. <see cref="NotificationService.Core.NotificationRateLimitPolicies"/>.</summary>

    public int StreamPermitLimit { get; init; } = 3;

    public int PreferencesUpdatePermitLimit { get; init; } = 10;
    public int PreferencesUpdateWindowMinutes { get; init; } = 1;

    public int BroadcastPermitLimit { get; init; } = 5;
    public int BroadcastWindowMinutes { get; init; } = 60;
}
