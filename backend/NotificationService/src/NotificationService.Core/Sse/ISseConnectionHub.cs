namespace NotificationService.Core.Sse;

/// <summary>
/// Внутрипроцессный hub активных SSE-соединений. Реализация — в слое Web
/// (держит <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/>).
/// Используется in-process consumer'ом для push'а вновь созданных уведомлений в браузер.
/// </summary>
public interface ISseConnectionHub
{
    /// <summary>
    /// Пушит событие всем активным соединениям данного пользователя. Если нет соединений — no-op.
    /// </summary>
    Task PushAsync(Guid userId, string eventName, string json, CancellationToken ct = default);
}
