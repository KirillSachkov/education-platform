using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Caching.Hybrid;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace NotificationService.Core.Messaging.Consumers.CacheInvalidation;

/// <summary>
/// Инвалидирует кеш material search-lookup при `material.updated`.
/// </summary>
public sealed class MaterialUpdatedCacheInvalidator
{
    private readonly HybridCache _cache;

    public MaterialUpdatedCacheInvalidator(HybridCache cache) => _cache = cache;

    public Task Handle(MaterialUpdated evt, CancellationToken ct) =>
        _cache.RemoveAsync(
            $"{CachedEducationContentServiceClient.MATERIAL_SEARCH_LOOKUP_KEY_PREFIX}{evt.MaterialId}",
            ct).AsTask();
}
