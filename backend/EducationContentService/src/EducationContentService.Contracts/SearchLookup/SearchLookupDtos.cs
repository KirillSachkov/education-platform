namespace EducationContentService.Contracts.SearchLookup;

public sealed record CourseSearchLookupDto(
    Guid Id,
    string Slug,
    string Title,
    string Description,
    PublicationStatus Status,
    DateTime UpdatedAt,
    IReadOnlyList<string> RequiredAccessTags,
    Guid? AuthorId = null);

public sealed record ModuleSearchLookupDto(
    Guid Id,
    Guid? CourseId,
    string? CourseSlug,
    string Title,
    string? Description,
    string? CourseTitle,
    PublicationStatus Status,
    DateTime UpdatedAt,
    IReadOnlyList<string> RequiredAccessTags,
    Guid? AuthorId = null);

public sealed record ProjectSearchLookupDto(
    Guid Id,
    Guid? CourseId,
    string? CourseSlug,
    string Title,
    string? Description,
    string? CourseTitle,
    PublicationStatus Status,
    DateTime UpdatedAt,
    IReadOnlyList<string> RequiredAccessTags,
    Guid? AuthorId = null);

public sealed record MaterialSearchLookupDto(
    Guid Id,
    Guid? CourseId,
    string? CourseSlug,
    string Title,
    Guid? ImageId,
    Guid? ModuleId,
    string? CourseTitle,
    string? ModuleTitle,
    PublicationStatus Status,
    IReadOnlyList<string> RequiredAccessTags,
    DateTime UpdatedAt,
    Guid? AuthorId = null,
    string? MaterialKind = null,
    string? Content = null,
    Guid? VideoId = null,
    IReadOnlyList<string>? ChapterTitles = null,
    IReadOnlyList<int>? ChapterTimestamps = null,
    // True когда материал привязан хотя бы к одному курсу, но НИ один из этих курсов
    // не PUBLISHED (все архивированы/draft) — search прячет такой документ (issue #378).
    // False для never-bound orphan'а (0 привязок) — он остаётся видимым (#77).
    bool IsCourseOrphaned = false);

public sealed record IssueSearchLookupDto(
    Guid Id,
    Guid ProjectId,
    Guid? CourseId,
    string? CourseSlug,
    string Title,
    Guid? ModuleId,
    string? CourseTitle,
    string? ProjectTitle,
    string? ModuleTitle,
    PublicationStatus Status,
    IReadOnlyList<string> RequiredAccessTags,
    DateTime UpdatedAt,
    Guid? AuthorId = null);

public sealed record CollectionSearchLookupDto(
    Guid Id,
    Guid? CourseId,
    string? CourseSlug,
    string Title,
    string? Description,
    Guid? ImageId,
    string? CourseTitle,
    PublicationStatus Status,
    IReadOnlyList<string> RequiredAccessTags,
    DateTime UpdatedAt,
    Guid? AuthorId = null);

/// <summary>
/// Id всех материалов, привязанных к курсу (course_materials ∪ module_items).
/// SearchService использует для каскадного пере-индекса видимости при archive/restore
/// курса (#378).
/// </summary>
public sealed record CourseMaterialIdsDto(IReadOnlyList<Guid> MaterialIds);
