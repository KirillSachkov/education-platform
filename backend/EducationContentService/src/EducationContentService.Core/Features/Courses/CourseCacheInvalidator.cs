using Microsoft.Extensions.Caching.Hybrid;

namespace EducationContentService.Core.Features.Courses;

/// <summary>
///     Centralizes removal of course curriculum and landing cache entries.
///     Must be called from every Course lifecycle mutation (Create/Update/Publish/Archive/Restore/Delete)
///     so that stale DTOs don't keep being served for the course's TTL (3 minutes).
/// </summary>
/// <remarks>
///     Cache keys are produced by <c>GetCurriculum.GetCacheKey</c> and <c>GetCourseLanding.GetCacheKey</c>.
///     There are three shared buckets per course: <c>anon</c>, <c>enrolled</c>, and <c>manage</c>.
///
///     <para>NOT invalidated here (by design):</para>
///     <list type="bullet">
///         <item><c>catalog:*</c> (GetCatalog) — public catalog list, 60s TTL staleness is
///         acceptable since the UX already treats catalog as periodically refreshing.</item>
///         <item><c>by-author:{authorId}:*</c> (GetAuthorCourses) — author dashboard list,
///         60s TTL staleness is acceptable (author's own edits feed back on next refresh).</item>
///     </list>
///     These entries expire naturally via their HybridCacheEntryOptions.Expiration window.
/// </remarks>
public static class CourseCacheInvalidator
{
    public static async Task InvalidateAsync(
        HybridCache cache,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        string[] keys =
        [
            $"curriculum:{courseId}:anon",
            $"curriculum:{courseId}:enrolled",
            $"curriculum:{courseId}:manage",
            $"course-landing:{courseId}:anon",
            $"course-landing:{courseId}:enrolled",
            $"course-landing:{courseId}:manage",
        ];

        foreach (string key in keys)
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
    }
}
