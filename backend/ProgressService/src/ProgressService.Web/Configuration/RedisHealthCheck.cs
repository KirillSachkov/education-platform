using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace ProgressService.Web.Configuration;

/// <summary>
///     Reports service unhealthy when Redis is unreachable. Redis is a hard dependency —
///     content access entitlements (IUserGrantWriter.GrantAsync/RevokeAsync) write to Redis.
///     Without this check, the service silently commits enrollments to Postgres while Redis
///     writes fail, and the health endpoint keeps returning 200.
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
