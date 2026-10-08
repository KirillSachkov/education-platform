namespace EducationContentService.Contracts.Materials;

public sealed record MaterialFeedItemDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string? Preview,
    string Kind,
    string Status,
    string AccessType,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? PublishedAt,
    Guid? ImageId,
    Guid? VideoId,
    string? ModuleTitle,
    Guid? CourseId,
    string? CourseTitle,
    string? CourseSlug)
{
    /// <summary>Resolved after Dapper materialization via batch FileService call.</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    /// Resolved post-SQL via IEntitlementChecker.CheckAccessBatchAsync.
    /// Default true for back-compat (existing callers that don't set it).
    /// </summary>
    public bool IsAccessible { get; init; } = true;

    /// <summary>
    /// "anonymous" | "not_enrolled" | "trial_required" | "standard_required" | null.
    /// Populated when IsAccessible=false so the UI can show the right CTA.
    /// </summary>
    public string? LockReason { get; init; }

    /// <summary>
    ///     Уникальные просмотры (auth + anon). Обогащается feed-handler'ом одним batch'ем
    ///     через <c>IProgressServiceClient</c> (HybridCache 5 min). 0 — если просмотров нет
    ///     или enrichment упал; деградация мягкая (никаких 5xx из-за бейджа). Issue #234.
    /// </summary>
    public long ViewsCount { get; init; }

    /// <summary>
    ///     Длительность привязанного видео в секундах (Kinescope). null — материал без видео
    ///     или метаданные ещё не готовы. Берётся из того же FileService batch'а, что и
    ///     thumbnail — дополнительных вызовов нет. Issue #500.
    /// </summary>
    public double? DurationSeconds { get; init; }
}

public static class MaterialLockReasons
{
    public const string Anonymous = "anonymous";
    public const string NotEnrolled = "not_enrolled";
    public const string TrialRequired = "trial_required";
    public const string StandardRequired = "standard_required";
}
