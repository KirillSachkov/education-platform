using EducationContentService.Core.Features.Courses;
using EducationContentService.Domain.Collections;
using Microsoft.Extensions.Caching.Hybrid;

namespace EducationContentService.Core.Features.Collections;

/// <summary>
///     Сбрасывает кеш curriculum/landing курса после lifecycle-мутации course-bound
///     подборки (#508): подборки отдаются в составе curriculum (блок «Подборки» на
///     странице программы и в курс-сайдбаре), и без инвалидации автор видел бы
///     «опубликовано» в toast'е, но в программе подборка появлялась бы только по TTL
///     (3 мин). Redis-сбой не должен превращаться в 500 — доменная операция уже
///     зафиксирована, в худшем случае кеш истечёт сам (паттерн Materials/Publish).
/// </summary>
public static class CollectionCourseCacheInvalidation
{
    public static async Task InvalidateAsync(
        HybridCache cache,
        Collection collection,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (collection.CourseId is null)
            return;

        try
        {
            await CourseCacheInvalidator.InvalidateAsync(cache, collection.CourseId.Value, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Curriculum cache invalidation failed after collection {CollectionId} mutation (will expire via TTL)",
                collection.Id);
        }
    }
}
