using StackExchange.Redis;

namespace AuthService.Core.Services;

/// <summary>
///     Redis-backed per-email rate limiter for OTP verification attempts.
///     Uses atomic Lua script (INCR + EXPIRE) for distributed rate limiting across multiple instances.
///     Fails CLOSED (denies attempt) if Redis is unavailable: without the counter we have no
///     defence against unbounded brute-force, so an outage of the limiter must not open the
///     door. Trade-off: a Redis outage temporarily blocks new OTP verifications for everyone
///     — acceptable because OTP send itself relies on Redis too, so the system is already in
///     degraded mode.
/// </summary>
public class OtpAttemptLimiter
{
    private const int MAX_ATTEMPTS = 5;
    private static readonly int _windowSeconds = (int)TimeSpan.FromMinutes(15).TotalSeconds;

    private static readonly LuaScript _incrementScript = LuaScript.Prepare(
        """
        local count = redis.call('INCR', @key)
        if count == 1 then
            redis.call('EXPIRE', @key, @ttl)
        end
        return count
        """);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<OtpAttemptLimiter> _logger;

    public OtpAttemptLimiter(IConnectionMultiplexer redis, ILogger<OtpAttemptLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    ///     Checks if the email has exceeded the OTP verification attempt limit.
    ///     Atomically increments the counter and sets TTL on first attempt via Lua script.
    /// </summary>
    /// <returns>true if the attempt is allowed; false if rate-limited.</returns>
    public virtual async Task<bool> TryAttemptAsync(string email)
    {
        try
        {
            string key = $"otp-attempts:{email.ToLowerInvariant()}";
            IDatabase db = _redis.GetDatabase();

            RedisResult result = await db.ScriptEvaluateAsync(
                _incrementScript,
                new { key = (RedisKey)key, ttl = _windowSeconds });

            long count = (long)result;
            return count <= MAX_ATTEMPTS;
        }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "Redis unavailable for OTP rate limiting, denying attempt (fail-closed)");
            return false;
        }
    }
}
