using System.Globalization;
using System.Security.Cryptography;
using StackExchange.Redis;

namespace AuthService.Core.Services;

/// <summary>
/// Redis-backed реализация <see cref="ITelegramLinkTokenStore"/>. Токен — 16 байт
/// случайных данных, закодированных в URL-safe base64 (22 символа). TTL 10 минут
/// задаётся на SET — нет ручной чистки, Redis сам удалит по истечении.
/// Чтение = <c>GET</c> (<see cref="PeekAsync"/>), удаление = <c>DEL</c> (<see cref="DeleteAsync"/>)
/// отдельным шагом после успешной привязки.
/// </summary>
public sealed class RedisTelegramLinkTokenStore : ITelegramLinkTokenStore
{
    private const int TOKEN_TTL_SECONDS = 600; // 10 минут
    private const int TOKEN_BYTES = 16;         // 128 бит энтропии → 22 base64url-символа
    private const string KEY_PREFIX = "telegram_link:";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisTelegramLinkTokenStore> _logger;

    public RedisTelegramLinkTokenStore(
        IConnectionMultiplexer redis,
        ILogger<RedisTelegramLinkTokenStore> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<string?> GenerateAsync(Guid userId)
    {
        try
        {
            string token = GenerateUrlSafeToken();
            IDatabase db = _redis.GetDatabase();

            bool stored = await db.StringSetAsync(
                KEY_PREFIX + token,
                userId.ToString("N", CultureInfo.InvariantCulture),
                TimeSpan.FromSeconds(TOKEN_TTL_SECONDS));

            if (!stored)
            {
                _logger.LogError("Failed to store telegram link token for user {UserId}", userId);
                return null;
            }

            return token;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error while generating telegram link token for user {UserId}", userId);
            return null;
        }
    }

    public async Task<Guid?> PeekAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            IDatabase db = _redis.GetDatabase();
            RedisValue raw = await db.StringGetAsync(KEY_PREFIX + token);

            if (!raw.HasValue)
                return null;

            if (Guid.TryParseExact(raw!, "N", out Guid userId))
                return userId;

            _logger.LogWarning("Malformed telegram link token payload: {Raw}", (string?)raw);
            return null;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error while reading telegram link token");
            return null;
        }
    }

    public async Task DeleteAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        try
        {
            IDatabase db = _redis.GetDatabase();
            await db.KeyDeleteAsync(KEY_PREFIX + token);
        }
        catch (RedisException ex)
        {
            // Best-effort: the link already committed; TTL will reap the token regardless.
            _logger.LogWarning(ex, "Redis error while deleting telegram link token");
        }
    }

    private static string GenerateUrlSafeToken()
    {
        Span<byte> bytes = stackalloc byte[TOKEN_BYTES];
        RandomNumberGenerator.Fill(bytes);

        // URL-safe base64 без padding — чтобы токен без проблем помещался в t.me/?start=.
        string b64 = Convert.ToBase64String(bytes);
        return b64
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
