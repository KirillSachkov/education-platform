using Microsoft.Extensions.Options;
using StackExchange.Redis;
using TelegramBotService.Core.Delivery;

namespace TelegramBotService.Web.Configuration;

/// <summary>
/// Redis-реализация <see cref="ITelegramIdempotencyStore"/>. Ключ:
/// <c>tg:delivery:{notificationId:N}:{chatId}</c>, значение — Telegram <c>message_id</c> (int).
/// TTL — конфигурируемый <see cref="TelegramIdempotencyOptions.TtlSeconds"/> (default 86400 = 1d).
/// Запись через SETNX (don't overwrite) — если две handler-replikи одновременно
/// сохранили message_id, первая wins, вторая silently skip.
/// </summary>
public sealed class RedisTelegramIdempotencyStore : ITelegramIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TelegramIdempotencyOptions _options;
    private readonly ILogger<RedisTelegramIdempotencyStore> _logger;

    public RedisTelegramIdempotencyStore(
        IConnectionMultiplexer redis,
        IOptions<TelegramIdempotencyOptions> options,
        ILogger<RedisTelegramIdempotencyStore> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int?> TryGetSentMessageIdAsync(Guid notificationId, long chatId, CancellationToken ct)
    {
        try
        {
            IDatabase db = _redis.GetDatabase();
            RedisValue value = await db.StringGetAsync(BuildKey(notificationId, chatId)).ConfigureAwait(false);
            if (value.IsNullOrEmpty)
                return null;

            return int.TryParse((string?)value, System.Globalization.CultureInfo.InvariantCulture, out int messageId)
                ? messageId
                : null;
        }
        catch (RedisException ex)
        {
            // Fail-open: Redis down → не блокируем delivery, просто рискуем потенциальным
            // дублем. Это приемлемо: дубль раз в сутки на 1000+ уведомлений лучше чем
            // полная остановка пайплайна доставки. Логируем для алертинга.
            _logger.LogWarning(
                ex, "Redis error reading idempotency key for {NotificationId}/{ChatId}; allowing delivery",
                notificationId, chatId);
            return null;
        }
    }

    public async Task SaveSentMessageIdAsync(Guid notificationId, long chatId, int messageId, CancellationToken ct)
    {
        try
        {
            IDatabase db = _redis.GetDatabase();
            await db.StringSetAsync(
                key: BuildKey(notificationId, chatId),
                value: messageId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                expiry: TimeSpan.FromSeconds(_options.TtlSeconds),
                when: When.NotExists).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            // Fail-open. См. TryGetSentMessageIdAsync — последствия те же.
            _logger.LogWarning(
                ex, "Redis error writing idempotency key for {NotificationId}/{ChatId}",
                notificationId, chatId);
        }
    }

    private static string BuildKey(Guid notificationId, long chatId) =>
        $"tg:delivery:{notificationId:N}:{chatId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Опции idempotency-кэша Telegram. Биндятся из секции <c>TelegramIdempotency</c>.
/// </summary>
public sealed class TelegramIdempotencyOptions
{
    public const string SECTION_NAME = "TelegramIdempotency";

    /// <summary>
    /// TTL ключа в Redis (секунд). Default 86400 (1 день). Дольше держать смысла нет —
    /// Wolverine inbox dedup-окно гораздо короче.
    /// </summary>
    public int TtlSeconds { get; init; } = 86_400;
}
