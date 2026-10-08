namespace ContentAccess;

public interface IResourceAccessWriter
{
    Task SetTagsAsync(string resourceType, Guid resourceId, IReadOnlyList<string> tags, CancellationToken ct = default);
    Task SetTagsManyAsync(IReadOnlyList<ResourceAccessWrite> writes, CancellationToken ct = default);
    Task AddTagAsync(string resourceType, Guid resourceId, string tag, CancellationToken ct = default);
    Task RemoveTagAsync(string resourceType, Guid resourceId, string tag, CancellationToken ct = default);
    Task ClearTagsAsync(string resourceType, Guid resourceId, CancellationToken ct = default);
}

public readonly record struct ResourceAccessWrite(
    string ResourceType,
    Guid ResourceId,
    IReadOnlyList<string> Tags);
