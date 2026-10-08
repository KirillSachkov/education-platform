namespace TelegramBotService.Core.Delivery;

/// <summary>
/// Кэш-стор для идемпотентности Telegram-доставки. Ключ — пара
/// (NotificationId, ChatId), значение — Telegram <c>message_id</c> успешно
/// отправленного сообщения. TTL — сутки (1 day по дефолту).
///
/// Зачем: <c>SendMessage</c> не имеет idempotency-key на стороне Telegram Bot API.
/// Если Wolverine retry'ит handler после успешной отправки (например, message
/// доехал в Telegram, ответ 200, но handler словил timeout до ack — на проде
/// видели Polly attempt с Execution Time = 50 секунд), без этого кэша пользователь
/// получит дубликат.
///
/// Реализации:
/// <list type="bullet">
/// <item><see cref="InMemoryTelegramIdempotencyStore"/> — fallback для тестов и dev'а без Redis.</item>
/// <item><c>RedisTelegramIdempotencyStore</c> (Web) — production, через <c>StackExchange.Redis</c>.</item>
/// </list>
/// </summary>
public interface ITelegramIdempotencyStore
{
    /// <summary>
    /// Возвращает закэшированный <c>message_id</c> если делavery уже была успешной;
    /// иначе <c>null</c>.
    /// </summary>
    Task<int?> TryGetSentMessageIdAsync(Guid notificationId, long chatId, CancellationToken ct);

    /// <summary>
    /// Сохраняет результат успешной отправки. Идемпотентен — повторная запись
    /// с тем же ключом не считается ошибкой.
    /// </summary>
    Task SaveSentMessageIdAsync(Guid notificationId, long chatId, int messageId, CancellationToken ct);
}

/// <summary>
/// In-memory реализация для тестов и dev'а без Redis. Не подходит для multi-replica
/// production — кэш не shared между репликами, не защищает от дублей.
/// </summary>
public sealed class InMemoryTelegramIdempotencyStore : ITelegramIdempotencyStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _cache = new(StringComparer.Ordinal);

    public Task<int?> TryGetSentMessageIdAsync(Guid notificationId, long chatId, CancellationToken ct)
    {
        string key = BuildKey(notificationId, chatId);
        return Task.FromResult(_cache.TryGetValue(key, out int messageId) ? messageId : (int?)null);
    }

    public Task SaveSentMessageIdAsync(Guid notificationId, long chatId, int messageId, CancellationToken ct)
    {
        string key = BuildKey(notificationId, chatId);
        _cache[key] = messageId;
        return Task.CompletedTask;
    }

    private static string BuildKey(Guid notificationId, long chatId) =>
        $"tg:delivery:{notificationId:N}:{chatId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}
