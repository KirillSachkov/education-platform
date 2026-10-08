using StackExchange.Redis;

namespace ContentAccess.Redis;

public sealed class RedisUserGrantWriter : IUserGrantWriter
{
    private readonly IConnectionMultiplexer _redis;

    public RedisUserGrantWriter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task GrantAsync(Guid userId, string tag, CancellationToken ct = default)
    {
        IDatabase db = _redis.GetDatabase();
        await db.SetAddAsync(EntitlementKeys.UserGrants(userId), tag);
    }

    public async Task GrantManyAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        IDatabase db = _redis.GetDatabase();
        RedisValue[] values = new RedisValue[tags.Count];
        for (int i = 0; i < tags.Count; i++)
            values[i] = tags[i];

        await db.SetAddAsync(EntitlementKeys.UserGrants(userId), values);
    }

    public async Task RevokeAsync(Guid userId, string tag, CancellationToken ct = default)
    {
        IDatabase db = _redis.GetDatabase();
        await db.SetRemoveAsync(EntitlementKeys.UserGrants(userId), tag);
    }

    public async Task RevokeManyAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        IDatabase db = _redis.GetDatabase();
        RedisValue[] values = new RedisValue[tags.Count];
        for (int i = 0; i < tags.Count; i++)
            values[i] = tags[i];

        await db.SetRemoveAsync(EntitlementKeys.UserGrants(userId), values);
    }

    public async Task ReplaceAsync(Guid userId, IReadOnlyList<string> tags, CancellationToken ct = default)
    {
        IDatabase db = _redis.GetDatabase();
        RedisKey key = EntitlementKeys.UserGrants(userId);

        ITransaction transaction = db.CreateTransaction();
        // Делаем DEL + SADD атомарно. Reader (SINTER) либо увидит старый снимок,
        // либо новый — никогда промежуточное пустое состояние.
        _ = transaction.KeyDeleteAsync(key);
        if (tags.Count > 0)
        {
            RedisValue[] values = new RedisValue[tags.Count];
            for (int i = 0; i < tags.Count; i++)
                values[i] = tags[i];
            _ = transaction.SetAddAsync(key, values);
        }
        await transaction.ExecuteAsync();
    }
}
