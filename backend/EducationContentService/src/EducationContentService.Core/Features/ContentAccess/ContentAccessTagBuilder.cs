using ContentAccess;
using EducationContentService.Domain;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Общий построитель Redis-тегов для уроков, заданий и материалов.
///     Учитывает три значения <see cref="AccessType"/>: PUBLIC, REGISTERED, ENROLLED.
///     После #358 FREE-tier удалён — бесплатный доступ = REGISTERED (system default).
///     Новые paid-гейты не привязаны к авторам: full-platform доступ = plan:all,
///     course-планы = plan:course:{id}.
/// </summary>
public static class ContentAccessTagBuilder
{
    /// <summary>
    ///     Возвращает набор тегов для ресурса (lesson/issue/material/collection)
    ///     в зависимости от его AccessType и набора курсов.
    ///
    ///     <list type="bullet">
    ///         <item>PUBLIC → <c>access:public</c> — доступен всем, включая анонимов.</item>
    ///         <item>REGISTERED → <c>authenticated</c> — любой залогиненный пользователь.</item>
    ///         <item>ENROLLED с курсами → global <c>plan:all</c> + per-course <c>plan:course:{X}</c>.</item>
    ///         <item>ENROLLED без курсов (orphan) → global <c>plan:all</c>.</item>
    ///     </list>
    /// </summary>
    public static IReadOnlyList<string> Build(
        AccessType accessType,
        Guid resourceId,
        IReadOnlyCollection<Guid> courseIds,
        ILogger? logger = null) =>
        Build(accessType.ToString(), resourceId, courseIds, logger);

    /// <summary>
    ///     Перегрузка для случаев, когда AccessType приходит строкой (Wolverine integration events).
    ///     Неизвестные значения получают sentinel-тег — безопасный fail-closed дефолт.
    ///     Legacy "FREE" (#358) коллапсируется в REGISTERED — same поведение как у новых REGISTERED-ресурсов;
    ///     data-миграция сама UPDATE'ит таблицы, но входящий event может ещё нести FREE до конца pipeline.
    /// </summary>
    public static IReadOnlyList<string> Build(
        string accessType,
        Guid resourceId,
        IReadOnlyCollection<Guid> courseIds,
        ILogger? logger = null)
    {
        if (string.Equals(accessType, "PUBLIC", StringComparison.OrdinalIgnoreCase))
        {
            return [GrantTags.PUBLIC];
        }

        if (string.Equals(accessType, "REGISTERED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(accessType, "FREE", StringComparison.OrdinalIgnoreCase))
        {
            // Legacy FREE collapsed into REGISTERED on read-path (defensive against
            // in-flight events that pre-date #358 data migration).
            return [GrantTags.AUTHENTICATED];
        }

        if (string.Equals(accessType, "ENROLLED", StringComparison.OrdinalIgnoreCase))
        {
            return BuildPlanScopedTags(accessType, resourceId, courseIds, logger);
        }

        logger?.LogWarning(
            "Resource {ResourceId} has unknown AccessType={AccessType}. Treating as restricted.",
            resourceId,
            accessType);

        return [GrantTags.ENROLLED_NO_COURSE];
    }

    public static IReadOnlyList<string> BuildMany(
        IEnumerable<string> accessTypes,
        Guid resourceId,
        IReadOnlyCollection<Guid> courseIds,
        ILogger? logger = null)
    {
        return accessTypes
            .SelectMany(accessType => Build(accessType, resourceId, courseIds, logger))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Default tags для курса/ресурса, у которого нет explicit AccessType (например,
    /// search-export резерв при пустом item-list). Все курсы для search-целей трактуются
    /// как PUBLIC, замок ставится только если у item'а явно задан AccessType ENROLLED.
    /// </summary>
    public static IReadOnlyList<string> BuildCoursePublicDefault() => [GrantTags.PUBLIC];

    /// <summary>
    ///     Теги доступа для course-ресурса (<c>resource-access:course:{id}</c>).
    ///     Гейтит «enrolled-эквивалент» видимость курса: приватные ENROLLED-материалы
    ///     в программе (<c>CanSeePrivateCourseArticles</c> в GetCurriculum/GetCourseLanding)
    ///     и комментирование course-bound контента (CommentService).
    ///
    ///     Набор: legacy <c>course:{id}</c> (его держит автор — выдаётся в CreateCourse —
    ///     плюс back-compat) + <c>plan:all</c> (global FULL/LEARN) +
    ///     <c>plan:course:{id}</c> (курсовой план).
    ///     PUBLIC/REGISTERED/анонимы намеренно НЕ включены — курсовой гейт даёт
    ///     enrolled-уровень доступа, не «любой залогиненный». Не зависит от AccessType
    ///     курса (в отличие от material/issue/collection).
    /// </summary>
    public static IReadOnlyList<string> BuildCourseAccessTags(Guid courseId)
    {
        return [GrantTags.Course(courseId), GrantTags.PlanAll(), GrantTags.PlanCourse(courseId)];
    }

    private static IReadOnlyList<string> BuildPlanScopedTags(
        string accessType,
        Guid resourceId,
        IReadOnlyCollection<Guid> courseIds,
        ILogger? logger)
    {
        if (courseIds.Count == 0)
        {
            logger?.LogWarning(
                "Resource {ResourceId} is {AccessType} orphan (no courses). Only global plan access tag will be set.",
                resourceId,
                accessType);

            return [GrantTags.PlanAll()];
        }

        Guid[] distinct = courseIds.Distinct().ToArray();
        List<string> result = new(distinct.Length + 1) { GrantTags.PlanAll() };

        foreach (Guid courseId in distinct)
        {
            // plan-tag aliases только. Legacy course-tag не эмитим — после Phase E
            // user-grants больше не накапливают `course:X[:trial]`, и SREM-cleanup
            // CLI снимает существующие.
            result.Add(GrantTags.PlanCourse(courseId));
        }

        return result.Distinct(StringComparer.Ordinal).ToArray();
    }
}
