using StackExchange.Redis;

namespace ContentAccess.Redis;

public sealed class RedisResourceAccessWriter : IResourceAccessWriter
{
    private readonly IConnectionMultiplexer _redis;

    public RedisResourceAccessWriter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task SetTagsAsync(string resourceType, Guid resourceId, IReadOnlyList<string> tags,
        CancellationToken ct = default)
    {
        IDatabase db = _redis.GetDatabase();
        RedisKey key = EntitlementKeys.ResourceAccess(resourceType, resourceId);

        await db.KeyDeleteAsync(key);

        if (tags.Count > 0)
        {
            RedisValue[] values = new RedisValue[tags.Count];
            for (int i = 0; i < tags.Count; i++)
                values[i] = tags[i];

            await db.SetAddAsync(key, values);
        }
    }

    public async Task SetTagsManyAsync(IReadOnlyList<ResourceAccessWrite> writes, CancellationToken ct = default)
    {
        if (writes.Count == 0)
            return;

        IDatabase db = _redis.GetDatabase();
        IBatch batch = db.CreateBatch();

        // Один round-trip на pipeline: DEL + SADD для каждой записи. Ордер внутри одной записи
        // сохраняется за счёт того, что оба await'а на одном IBatch идут в порядке добавления.
        List<Task> tasks = new(writes.Count * 2);
        foreach (ResourceAccessWrite write in writes)
        {
            RedisKey key = EntitlementKeys.ResourceAccess(write.ResourceType, write.ResourceId);
            tasks.Add(batch.KeyDeleteAsync(key));

            if (write.Tags.Count > 0)
            {
                RedisValue[] values = new RedisValue[write.Tags.Count];
                for (int i = 0; i < write.Tags.Count; i++)
                    values[i] = write.Tags[i];

                tasks.Add(batch.SetAddAsync(key, values));
            }
        }

        batch.Execute();
        await Task.WhenAll(tasks);
    }

    public async Task AddTagAsync(string resourceType, Guid resourceId, string tag, CancellationToken ct = default)
    {
        IDatabase db = _redis.GetDatabase();
        await db.SetAddAsync(EntitlementKeys.ResourceAccess(resourceType, resourceId), tag);
    }

    public async Task RemoveTagAsync(string resourceType, Guid resourceId, string tag, CancellationToken ct = default)
    {
        IDatabase db = _redis.GetDatabase();
        await db.SetRemoveAsync(EntitlementKeys.ResourceAccess(resourceType, resourceId), tag);
    }

    public async Task ClearTagsAsync(string resourceType, Guid resourceId, CancellationToken ct = default)
    {
        IDatabase db = _redis.GetDatabase();
        await db.KeyDeleteAsync(EntitlementKeys.ResourceAccess(resourceType, resourceId));
    }
}
