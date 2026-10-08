using StackExchange.Redis;
using TelegramBotService.Core.Features.CourseChats.Services;

namespace TelegramBotService.Web.Configuration;

/// <summary>
/// Redis-реализация <see cref="IPlanWelcomeSentStore"/>. Ключ:
/// <c>tg:welcome:{planId:N}:{dm|group}:{telegramUserId}</c>, значение — <c>"1"</c>, БЕЗ TTL
/// (семантика «приветствие один раз навсегда per destination»; кардинальность ограничена
/// платящими юзерами × планами × 2). Destination в ключе разводит DM и GROUP, чтобы раннее
/// DM-приветствие не подавляло позднее групповое (и наоборот, #687). Fail-open: при ошибке
/// Redis считаем «не отправлено» — допускаем редкий дубль приветствия вместо его подавления
/// (подавление welcome — баг #444).
/// </summary>
public sealed class RedisPlanWelcomeSentStore : IPlanWelcomeSentStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisPlanWelcomeSentStore> _logger;

    public RedisPlanWelcomeSentStore(
        IConnectionMultiplexer redis,
        ILogger<RedisPlanWelcomeSentStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<bool> TryMarkSentAsync(
        Guid planId, long telegramUserId, WelcomeDestination destination, CancellationToken ct)
    {
        try
        {
            IDatabase db = _redis.GetDatabase();
            return await db.StringSetAsync(
                key: InMemoryPlanWelcomeSentStore.BuildKey(planId, telegramUserId, destination),
                value: "1",
                expiry: null,
                when: When.NotExists).ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(
                ex, "Redis error claiming plan-welcome key for {PlanId}/{Dest}/{UserId}; treating as claimed",
                planId, destination, telegramUserId);
            return true;
        }
    }

    public async Task UnmarkSentAsync(
        Guid planId, long telegramUserId, WelcomeDestination destination, CancellationToken ct)
    {
        try
        {
            IDatabase db = _redis.GetDatabase();
            await db.KeyDeleteAsync(
                    InMemoryPlanWelcomeSentStore.BuildKey(planId, telegramUserId, destination))
                .ConfigureAwait(false);
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(
                ex, "Redis error releasing plan-welcome key for {PlanId}/{Dest}/{UserId}",
                planId, destination, telegramUserId);
        }
    }
}
