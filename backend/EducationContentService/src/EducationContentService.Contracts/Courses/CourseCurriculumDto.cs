namespace EducationContentService.Contracts.Courses;

public sealed record CourseCurriculumDto(
    Guid Id,
    Guid AuthorId,
    string Slug,
    string Title,
    string Description,
    string Status,
    string Kind,
    Guid? ImageId,
    string? ImageUrl,
    Guid? GettingStartedModuleId,
    bool HasFreeContent,
    bool IsNew,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<CurriculumSectionDto> Sections,
    IReadOnlyList<string> LearningOutcomes,
    IReadOnlyList<string> TargetAudience,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<CurriculumCollectionDto> Collections,
    // Author byline (#569) — display name + avatar URL for the public course-overview page
    // (CourseHome hero). Enriched on the backend via IAuthorLookupClient; best-effort (null
    // when AuthService is degraded / no avatar). Trailing-optional to keep ctor calls valid.
    string? AuthorDisplayName = null,
    string? AuthorAvatarUrl = null);

public sealed record CurriculumSectionDto(
    Guid Id,
    string ItemType,
    string Title,
    string? Description,
    string? DetailedDescription,
    string SortKey,
    bool IsOptional,
    IReadOnlyList<CurriculumItemDto> Items);

public sealed record CurriculumItemDto(
    Guid Id,
    string ItemType,
    string Title,
    string SortKey,
    bool IsOptional,
    int Position = 0,
    string? AccessType = null,
    string? ViewPriority = null,
    string? MaterialKind = null,
    double? DurationSeconds = null,
    string? CoverUrl = null,
    int? QuestionsCount = null,
    Guid? QuizId = null);

/// <summary>
///     Опубликованная подборка курса в программе (#508). <c>ItemsCount</c> и
///     <c>MaterialIds</c> считают только PUBLISHED-материалы — согласовано со
///     знаменателями прогресс-blueprint'а (#496), чтобы «X из Y» на карточке
///     подборки сходился с общим счётчиком курса. <c>MaterialIds</c> нужны фронту
///     для локального пересчёта X через learning-state (никакого нового
///     cross-service вызова): viewed = learningState.materials ∩ MaterialIds.
/// </summary>
public sealed record CurriculumCollectionDto(
    Guid Id,
    string Title,
    string? Description,
    string AccessType,
    int ItemsCount,
    IReadOnlyList<Guid> MaterialIds,
    string? CoverUrl);
