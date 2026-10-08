using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Caching.Hybrid;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace NotificationService.Core.Messaging.Consumers.CacheInvalidation;

/// <summary>
/// Инвалидирует кеш issue search-lookup при `issue.updated`.
/// </summary>
public sealed class IssueUpdatedCacheInvalidator
{
    private readonly HybridCache _cache;

    public IssueUpdatedCacheInvalidator(HybridCache cache) => _cache = cache;

    public Task Handle(IssueUpdated evt, CancellationToken ct) =>
        _cache.RemoveAsync(
            $"{CachedEducationContentServiceClient.ISSUE_SEARCH_LOOKUP_KEY_PREFIX}{evt.IssueId}",
            ct).AsTask();
}
