namespace NotificationService.Core.Sse;

/// <summary>
/// Абстракция над Redis Pub/Sub для fan-out'а SSE-событий между репликами NotificationService.
///
/// <para>
/// <b>Зачем:</b> SSE-соединения хранятся в памяти конкретной реплики. Если <c>NotificationCreated</c>
/// пришёл на Replica A, а SSE-соединение user'а — на Replica B, без Redis событие не дойдёт до клиента.
/// </para>
///
/// <para>
/// <b>Поток:</b> Replica X получает <c>NotificationCreated</c> (Wolverine) → публикует в Redis
/// канал <c>notifications:sse:user:{userId}</c> (JSON payload). <c>SseRedisSubscriberService</c>
/// на всех репликах (включая X) subscribe'ится и пушит событие в локальный <see cref="ISseConnectionHub"/>.
/// </para>
///
/// <para>
/// <b>Реализация:</b> <c>RedisSseFanoutPublisher</c> (Web) через <c>IConnectionMultiplexer</c>,
/// либо <c>NullSseRedisPublisher</c> (Core) как no-op fallback когда Redis fanout disabled
/// (single-replica dev). Выбор определяется <c>SseOptions.RedisFanoutEnabled</c>.
/// </para>
/// </summary>
public interface ISseRedisPublisher
{
    /// <summary>
    /// <c>true</c> если публикация в Redis включена (multi-replica). Fanout-handler в этом случае
    /// НЕ push'ит напрямую в local hub — ждёт прихода через subscriber, чтобы не было дублей.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Публикует событие в Redis канал. No-op если <see cref="IsEnabled"/> = false.
    /// </summary>
    Task PublishAsync(Guid userId, string eventName, string json, CancellationToken ct = default);
}
