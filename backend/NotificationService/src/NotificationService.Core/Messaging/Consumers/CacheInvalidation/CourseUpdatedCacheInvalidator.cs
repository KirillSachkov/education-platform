using EducationContentService.Contracts.HttpCommunication;
using Microsoft.Extensions.Caching.Hybrid;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace NotificationService.Core.Messaging.Consumers.CacheInvalidation;

/// <summary>
/// Инвалидирует кеш <see cref="CachedEducationContentServiceClient"/> при `course.updated`.
/// Удаляет обе вариации ключа — `GetCourseLookupAsync` (CourseDto) и `GetCourseSearchLookupAsync`
/// (CourseSearchLookupDto), потому что любая часть могла быть отрендерена в чей-то
/// уведомительный текст.
/// </summary>
public sealed class CourseUpdatedCacheInvalidator
{
    private readonly HybridCache _cache;

    public CourseUpdatedCacheInvalidator(HybridCache cache) => _cache = cache;

    public async Task Handle(CourseUpdated evt, CancellationToken ct)
    {
        await _cache.RemoveAsync(
            $"{CachedEducationContentServiceClient.COURSE_LOOKUP_KEY_PREFIX}{evt.CourseId}", ct);
        await _cache.RemoveAsync(
            $"{CachedEducationContentServiceClient.COURSE_SEARCH_LOOKUP_KEY_PREFIX}{evt.CourseId}", ct);
    }
}
