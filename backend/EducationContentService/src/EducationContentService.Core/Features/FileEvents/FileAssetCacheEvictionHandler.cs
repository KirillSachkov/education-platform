using Microsoft.Extensions.Caching.Hybrid;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace EducationContentService.Core.Features.FileEvents;

public sealed class FileAssetCacheEvictionHandler
{
    private readonly HybridCache _cache;

    public FileAssetCacheEvictionHandler(HybridCache cache)
    {
        _cache = cache;
    }

    public async Task Handle(FileAssetDeleted message, CancellationToken ct)
    {
        if (string.Equals(message.Kind, "video", StringComparison.Ordinal))
        {
            await _cache.RemoveAsync(
                $"{CachedFileServiceClient.VIDEO_DETAIL_CACHE_KEY_PREFIX}{message.AssetId}", ct);
            await _cache.RemoveAsync(
                $"{CachedFileServiceClient.VIDEO_PUBLIC_CACHE_KEY_PREFIX}{message.AssetId}", ct);
        }
        else
        {
            await _cache.RemoveAsync(
                $"{CachedFileServiceClient.FILE_CACHE_KEY_PREFIX}{message.AssetId}", ct);
        }
    }
}
