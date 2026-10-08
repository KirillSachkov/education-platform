using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Shared.GitHubApp;

/// <summary>
///     Redis-backed реализация — корректно работает в multi-instance prod.
///     Каждый state-token хранится как <c>github_app:install_state:{keyspace}:{token}</c>
///     с JSON-serialized <typeparamref name="TData"/> и Redis-native TTL.
///
///     <paramref name="keyspace"/> — короткий per-service namespace (например,
///     <c>access</c> или <c>ars</c>) для изоляции между сервисами.
/// </summary>
public sealed class RedisInstallStateStore<TData> : IInstallStateStore<TData>
    where TData : class
{
    private const string KEY_PREFIX = "github_app:install_state:";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisInstallStateStore<TData>> _logger;
    private readonly string _keyspace;

    public RedisInstallStateStore(
        IConnectionMultiplexer redis,
        ILogger<RedisInstallStateStore<TData>> logger,
        string keyspace)
    {
        _redis = redis;
        _logger = logger;
        _keyspace = keyspace;
    }

    public async Task SetAsync(string stateToken, TData data, TimeSpan ttl)
    {
        try
        {
            IDatabase db = _redis.GetDatabase();
            string key = BuildKey(stateToken);
            string value = JsonSerializer.Serialize(data);
            await db.StringSetAsync(key, value, ttl);
        }
        catch (RedisException ex)
        {
            // Fail closed — лучше пользователю получить state.invalid и retry, чем
            // потеряем state без TTL (security risk при stuck state).
            _logger.LogError(ex, "Failed to write install state to Redis (keyspace={Keyspace}, token={TokenPrefix}…)",
                _keyspace, stateToken[..Math.Min(stateToken.Length, 8)]);
            throw;
        }
    }

    public async Task<TData?> ConsumeAsync(string stateToken)
    {
        try
        {
            IDatabase db = _redis.GetDatabase();
            string key = BuildKey(stateToken);
            // GETDEL атомарно read+delete — single-use semantics.
            RedisValue value = await db.StringGetDeleteAsync(key);
            if (!value.HasValue) return null;

            return JsonSerializer.Deserialize<TData>(value.ToString());
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to consume install state from Redis (keyspace={Keyspace}, token={TokenPrefix}…)",
                _keyspace, stateToken[..Math.Min(stateToken.Length, 8)]);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Corrupted install state JSON in Redis (keyspace={Keyspace}, token={TokenPrefix}…)",
                _keyspace, stateToken[..Math.Min(stateToken.Length, 8)]);
            return null;
        }
    }

    private string BuildKey(string stateToken) => $"{KEY_PREFIX}{_keyspace}:{stateToken}";
}
