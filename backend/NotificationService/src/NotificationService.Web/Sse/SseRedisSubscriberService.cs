using NotificationService.Core.Sse;
using StackExchange.Redis;

namespace NotificationService.Web.Sse;

/// <summary>
/// Фоновый сервис: subscribe на Redis-pattern <see cref="SseRedisChannel.PATTERN"/> и push
/// полученных событий в локальный <see cref="SseConnectionHub"/>.
///
/// <para>
/// Запускается только если <c>ISseRedisPublisher</c> зарегистрирован как
/// <see cref="RedisSseFanoutPublisher"/> (флаг <c>Sse:RedisFanoutEnabled = true</c>).
/// Иначе регистрация пропускается и этот сервис не стартует.
/// </para>
///
/// <para>
/// Connection multiplexer shared с другими Redis-consumer'ами (ContentAccess reader/writer,
/// HybridCache). StackExchange.Redis справляется с множеством subscribe'ов на одном соединении.
/// </para>
/// </summary>
public sealed class SseRedisSubscriberService : BackgroundService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly SseConnectionHub _hub;
    private readonly ILogger<SseRedisSubscriberService> _logger;

    public SseRedisSubscriberService(
        IConnectionMultiplexer redis,
        SseConnectionHub hub,
        ILogger<SseRedisSubscriberService> logger)
    {
        _redis = redis;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ISubscriber subscriber = _redis.GetSubscriber();
        RedisChannel pattern = RedisChannel.Pattern(SseRedisChannel.PATTERN);

        await subscriber.SubscribeAsync(pattern, HandleMessage);
        _logger.LogInformation(
            "SseRedisSubscriberService: subscribed to pattern {Pattern}", SseRedisChannel.PATTERN);

        try
        {
            // Держим сервис живым, пока не стоп. Redis подписка висит в multiplexer'е.
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
        finally
        {
            try
            {
                await subscriber.UnsubscribeAsync(pattern);
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogWarning(ex, "Failed to unsubscribe from Redis pattern {Pattern}", SseRedisChannel.PATTERN);
            }
        }
    }

    private void HandleMessage(RedisChannel channel, RedisValue value)
    {
        try
        {
            Guid? userId = SseRedisChannel.TryExtractUserId(channel.ToString());
            if (userId is null)
                return;

            string raw = value.ToString();
            int sep = raw.IndexOf('\n', StringComparison.Ordinal);
            if (sep < 0)
                return;

            string eventName = raw[..sep];
            string json = raw[(sep + 1)..];

            // Fire-and-forget push — это sync callback от StackExchange.Redis,
            // не хотим держать event-loop'а подписчика.
            _ = _hub.PushAsync(userId.Value, eventName, json);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogWarning(
                ex, "SSE Redis subscriber: failed to dispatch message on channel {Channel}", channel);
        }
    }
}
