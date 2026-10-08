using NotificationService.Core.Sse;
using StackExchange.Redis;

namespace NotificationService.Web.Sse;

/// <summary>
/// <see cref="ISseRedisPublisher"/>-имплементация через <c>IConnectionMultiplexer.GetSubscriber()</c>.
///
/// <para>
/// Формат сообщения — JSON с полями <c>event</c> и <c>data</c> (строка, т.к. подписчику удобнее
/// не распаковывать вложенные структуры). Channel: <see cref="SseRedisChannel.For"/>.
/// </para>
/// </summary>
public sealed class RedisSseFanoutPublisher : ISseRedisPublisher
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisSseFanoutPublisher> _logger;

    public RedisSseFanoutPublisher(
        IConnectionMultiplexer redis,
        ILogger<RedisSseFanoutPublisher> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public bool IsEnabled => true;

    public async Task PublishAsync(
        Guid userId,
        string eventName,
        string json,
        CancellationToken ct = default)
    {
        try
        {
            ISubscriber subscriber = _redis.GetSubscriber();
            RedisChannel channel = RedisChannel.Literal(SseRedisChannel.For(userId));

            // Сериализуем "eventName|json" одной строкой — лёгкий формат, без вложенного JSON,
            // парсится split'ом в subscriber'е.
            string payload = eventName + "\n" + json;

            await subscriber.PublishAsync(channel, payload);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Redis недоступен — событие не дойдёт до других реплик, но текущая реплика SSE
            // не имеет данных. Не роняем handler: best-effort SSE лучше чем dead-letter.
            _logger.LogWarning(
                ex,
                "Failed to publish SSE event to Redis for user {UserId}; other replicas will miss this event",
                userId);
        }
    }
}
