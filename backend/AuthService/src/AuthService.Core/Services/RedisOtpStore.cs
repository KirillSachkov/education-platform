using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace AuthService.Core.Services;

public interface IOtpStore
{
    Task<string?> GenerateAndStoreAsync(string email);
    Task<bool> VerifyAndConsumeAsync(string email, string code);
}

/// <summary>
/// Redis-backed OTP code storage. Generates 6-digit codes with 5-minute TTL.
/// Codes auto-expire via Redis TTL — no manual timestamp tracking needed.
/// Verify+consume uses an atomic Lua script (GET then DEL-iff-match) so the same
/// code cannot be successfully consumed twice by concurrent requests, and a wrong
/// guess does NOT invalidate the still-pending code (the user can retry until the
/// attempt limiter trips or the TTL expires).
/// </summary>
public sealed class RedisOtpStore : IOtpStore
{
    private const int CODE_TTL_SECONDS = 300; // 5 minutes

    // Lua: get stored code; if it matches the supplied one, atomically delete and
    // return "1". Otherwise return "0" (and keep the stored code intact so the user
    // can retry within the rate-limit budget). Runs in a single Redis call —
    // no race between GET and DEL.
    private static readonly LuaScript _verifyAndConsumeScript = LuaScript.Prepare(
        """
        local stored = redis.call('GET', @key)
        if not stored then
            return '0'
        end
        if stored == @code then
            redis.call('DEL', @key)
            return '1'
        end
        return '0'
        """);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisOtpStore> _logger;

    public RedisOtpStore(IConnectionMultiplexer redis, ILogger<RedisOtpStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    /// <summary>
    /// Generates a 6-digit OTP code, stores it in Redis with TTL, and returns the code.
    /// </summary>
    public async Task<string?> GenerateAndStoreAsync(string email)
    {
        try
        {
            string code = RandomNumberGenerator.GetInt32(100_000, 1_000_000)
                .ToString(CultureInfo.InvariantCulture);

            string key = GetKey(email);
            IDatabase db = _redis.GetDatabase();
            await db.StringSetAsync(key, code, TimeSpan.FromSeconds(CODE_TTL_SECONDS));

            return code;
        }
        catch (RedisException ex)
        {
            // Email-hash, not raw email — operational logs are not the audit channel and
            // emails are PII (152-ФЗ). AuthAuditLog uses the same hashing pattern.
            _logger.LogError(ex, "Failed to store OTP code in Redis for {EmailHash}", HashEmail(email));
            return null;
        }
    }

    /// <summary>
    /// Atomically verifies the OTP code and consumes it on match (one-time use).
    /// Returns true if the code is valid. Expiry is handled by Redis TTL.
    /// </summary>
    public async Task<bool> VerifyAndConsumeAsync(string email, string code)
    {
        try
        {
            string key = GetKey(email);
            IDatabase db = _redis.GetDatabase();

            RedisResult result = await db.ScriptEvaluateAsync(
                _verifyAndConsumeScript,
                new { key = (RedisKey)key, code });

            return string.Equals((string?)result, "1", StringComparison.Ordinal);
        }
        catch (RedisException ex)
        {
            _logger.LogError(
                ex, "Failed to verify OTP code from Redis for {EmailHash}", HashEmail(email));
            return false;
        }
    }

    private static string GetKey(string email) => $"otp:{email.ToLowerInvariant()}";

    private static string HashEmail(string email)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(email.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..12];
    }
}
