using System.Diagnostics;
using StackExchange.Redis;

namespace ContentAccess.Redis;

public sealed class RedisEntitlementReader : IEntitlementReader
{
    private readonly IConnectionMultiplexer _redis;

    public RedisEntitlementReader(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<EntitlementGrantSet> GetUserGrantTagsAsync(Guid userId, CancellationToken ct = default)
    {
        if (userId == Guid.Empty)
        {
            return EntitlementGrantSet.Empty;
        }

        using Activity? activity = ContentAccessDiagnostics.ActivitySource.StartActivity(
            "contentaccess.read_user_grants", ActivityKind.Internal);
        activity?.SetTag("user.id", userId);

        IDatabase db = _redis.GetDatabase();
        RedisValue[] grants = await db.SetMembersAsync(EntitlementKeys.UserGrants(userId));
        if (grants.Length == 0)
        {
            activity?.SetTag("user.grants.size", 0);
            return EntitlementGrantSet.Empty;
        }

        string[] tags = grants
            .Select(static grant => grant.ToString())
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        activity?.SetTag("user.grants.size", tags.Length);

        return tags.Length == 0
            ? EntitlementGrantSet.Empty
            : new EntitlementGrantSet(tags);
    }
}
