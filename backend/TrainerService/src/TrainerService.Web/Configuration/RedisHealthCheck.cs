using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace TrainerService.Web.Configuration;

/// <summary>
///     Health-check Redis (cap:TRAINER_PRO entitlements, #614) — PING. Per-service local copy
///     (mirrors CommentService/AccessService); ContentAccess не экспортит готовый health-check.
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            IDatabase db = _redis.GetDatabase();
            await db.PingAsync();
            return HealthCheckResult.Healthy("Redis connection is healthy");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Redis connection failed", ex);
        }
    }
}
