using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace AssignmentReviewService.Core.AiSettings;

/// <summary>
///     Подписчик на Redis pub/sub channel для cross-replica AI-settings cache
///     invalidation. На каждое сообщение дропает L1 (<see cref="IMemoryCache"/>)
///     ключ singleton'а — следующий <c>ResolveReviewerAsync</c> подтянет свежее
///     значение из БД.
///
///     <para>
///         Singleton-hosted-service: одна подписка на pod. Если
///         <see cref="IConnectionMultiplexer"/> не зарегистрирован (тесты,
///         или Redis dynamically disabled), сервис no-op стартует — admin
///         update'ы будут видны только в локальном L1 cache до истечения TTL.
///     </para>
///
///     <para>Issue #328 (ARS hardening, post-#320).</para>
/// </summary>
internal sealed class AiSettingsCacheInvalidationSubscriber : IHostedService
{
    private readonly IMemoryCache _cache;
    private readonly IConnectionMultiplexer? _redis;
    private readonly ILogger<AiSettingsCacheInvalidationSubscriber> _logger;
    private ChannelMessageQueue? _subscription;

    public AiSettingsCacheInvalidationSubscriber(
        IMemoryCache cache,
        ILogger<AiSettingsCacheInvalidationSubscriber> logger,
        IConnectionMultiplexer? redis = null)
    {
        _cache = cache;
        _logger = logger;
        _redis = redis;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_redis is null)
        {
            _logger.LogInformation(
                "Redis not registered — AI settings cache invalidation will be local-only");
            return;
        }

        try
        {
            _subscription = await _redis.GetSubscriber()
                .SubscribeAsync(AssignmentReviewAiModelSettingsResolver.INVALIDATION_CHANNEL);
            _subscription.OnMessage(_ =>
            {
                _cache.Remove(AssignmentReviewAiModelSettingsResolver.CACHE_KEY);
                _logger.LogDebug("AI settings cache invalidated via Redis pub/sub");
            });
            _logger.LogInformation("Subscribed to AI settings invalidation channel");
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex,
                "Failed to subscribe to AI settings invalidation channel — falling back to TTL-only invalidation");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_subscription is null) return;

        try
        {
            await _subscription.UnsubscribeAsync();
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Failed to unsubscribe from AI settings invalidation channel");
        }
    }
}
