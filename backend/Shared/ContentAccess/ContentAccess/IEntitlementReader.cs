namespace ContentAccess;

public interface IEntitlementReader
{
    Task<EntitlementGrantSet> GetUserGrantTagsAsync(Guid userId, CancellationToken ct = default);
}
