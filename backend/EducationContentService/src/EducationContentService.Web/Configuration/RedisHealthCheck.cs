using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace EducationContentService.Web.Configuration;

/// <summary>
///     Redis is a hard dependency for material/course access tags used by entitlement checks.
///     If Redis is unreachable, the service must report unhealthy instead of advertising a false-ready state.
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
