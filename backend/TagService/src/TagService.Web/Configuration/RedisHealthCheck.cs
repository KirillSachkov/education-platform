using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace TagService.Web.Configuration;

/// <summary>
///     TagService publishes integration events and also depends on Redis-backed content-access helpers.
///     If Redis is unavailable, readiness must fail instead of reporting a false-green health state.
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
            if (!_redis.IsConnected)
            {
                return HealthCheckResult.Unhealthy("Redis connection is not established");
            }

            IDatabase db = _redis.GetDatabase();
            await db.PingAsync();
            return HealthCheckResult.Healthy("Redis connection is healthy");
        }
        catch (RedisException ex)
        {
            return HealthCheckResult.Unhealthy("Redis ping failed", ex);
        }
        catch (TimeoutException ex)
        {
            return HealthCheckResult.Unhealthy("Redis ping timed out", ex);
        }
    }
}
