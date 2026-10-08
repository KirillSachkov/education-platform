namespace ContentAccess;

public static class GrantTags
{
    /// <summary>
    /// Явный маркер публичного ресурса. Присутствует в наборе тегов ресурса с
    /// AccessType=PUBLIC. Нужен, чтобы отличать PUBLIC от отсутствующей записи
    /// в Redis (fail-closed семантика).
    /// </summary>
    public const string PUBLIC = "access:public";

    public const string AUTHENTICATED = "authenticated";
    public const string ENROLLED_NO_COURSE = "enrolled:unassigned";
    public const string COURSE_PREFIX = "course:";
    public const string TRIAL_SUFFIX = ":trial";

    /// <summary>
    /// Plan-grant tag prefix for COURSES kind plans. User получает этот тег при
    /// активном PlanGrant с курсом в scope. Wired в Phase 4; consumer-side полное
    /// использование — Phase 9 cutover.
    /// </summary>
    public const string PLAN_COURSE_PREFIX = "plan:course:";

    /// <summary>
    /// Global platform-wide plan tag for FULL_ALL / LEARN_ALL grants.
    /// </summary>
    public const string PLAN_ALL = "plan:all";

    /// <summary>
    /// Legacy plan-grant tag prefix for author-scoped lifetime plans. Kept for
    /// compatibility with existing Redis/resource tags until a full resync.
    /// </summary>
    public const string PLAN_LIFETIME_PREFIX = "plan:lifetime:author_";

    /// <summary>
    /// Capability-tag prefix. Записывается в <c>user-grants:{userId}</c> вместе с
    /// plan-tags при создании grant'а — кодирует индивидуальные флаги из
    /// <c>PlanCapabilities</c>: <c>cap:SUBMIT_ISSUES</c>, <c>cap:COMMUNITY_ACCESS</c>
    /// и т.д. Используется для гейтинга действий после доступа к ресурсу.
    /// </summary>
    public const string CAPABILITY_PREFIX = "cap:";

    public static string Course(Guid courseId) => $"course:{courseId:D}";
    public static string CourseTrial(Guid courseId) => $"course:{courseId:D}:trial";
    public static string Lesson(Guid lessonId) => $"lesson:{lessonId:D}";
    public static string Tier(string tier) => $"tier:{tier}";

    public static string PlanCourse(Guid courseId) => $"{PLAN_COURSE_PREFIX}{courseId:D}";

    public static string PlanAll() => PLAN_ALL;

    public static string PlanLifetime(Guid authorId) => $"{PLAN_LIFETIME_PREFIX}{authorId:D}";

    public static string Capability(string capabilityName) => $"{CAPABILITY_PREFIX}{capabilityName}";

    public static bool IsCourseGrant(string value) =>
        value.StartsWith(COURSE_PREFIX, StringComparison.Ordinal) &&
        !value.EndsWith(TRIAL_SUFFIX, StringComparison.Ordinal);

    public static bool IsCourseTrialGrant(string value) =>
        value.StartsWith(COURSE_PREFIX, StringComparison.Ordinal) &&
        value.EndsWith(TRIAL_SUFFIX, StringComparison.Ordinal);

    public static bool IsPlanCourseGrant(string value) =>
        value.StartsWith(PLAN_COURSE_PREFIX, StringComparison.Ordinal);

    public static bool IsPlanAllGrant(string value) =>
        string.Equals(value, PLAN_ALL, StringComparison.Ordinal);

    public static bool IsPlanLifetimeGrant(string value) =>
        value.StartsWith(PLAN_LIFETIME_PREFIX, StringComparison.Ordinal);

    public static Guid ParseCourseGrant(string value) =>
        Guid.Parse(value[COURSE_PREFIX.Length..]);

    public static Guid ParseCourseTrialGrant(string value) =>
        Guid.Parse(value[COURSE_PREFIX.Length..^TRIAL_SUFFIX.Length]);

    public static Guid ParsePlanCourseGrant(string value) =>
        Guid.Parse(value[PLAN_COURSE_PREFIX.Length..]);

    public static Guid ParsePlanLifetimeGrant(string value) =>
        Guid.Parse(value[PLAN_LIFETIME_PREFIX.Length..]);
}
