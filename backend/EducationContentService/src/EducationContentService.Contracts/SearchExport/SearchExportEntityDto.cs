using Common;

namespace EducationContentService.Contracts.SearchExport;

public sealed class SearchExportEntityDto
{
    public EntityType EntityType { get; init; }

    public Guid EntityId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public Guid? ImageId { get; init; }

    public IReadOnlyList<string> RequiredAccessTags { get; init; } = [];

    public Guid? CourseId { get; init; }

    public string? CourseSlug { get; init; }

    public string? CourseTitle { get; init; }

    public Guid? ProjectId { get; init; }

    public string? ProjectTitle { get; init; }

    public Guid? ModuleId { get; init; }

    public string? ModuleTitle { get; init; }

    public DateTime UpdatedAt { get; init; }

    public Guid? AuthorId { get; init; }

    /// <summary>
    /// Kind материала (ARTICLE / VIDEO / NOTE / STREAM). Только для EntityType.Material.
    /// </summary>
    public string? MaterialKind { get; init; }

    /// <summary>
    /// Markdown-тело материала (первые 200 КБ). Используется только для индексации
    /// полнотекстового поиска. У остальных entity_type null. Не хранится в ответе поиска.
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Kinescope VideoId материала (только для kind=VIDEO). Нужен, чтобы поиск мог
    /// резолвнуть реальный thumbnail через FileService batch.
    /// </summary>
    public Guid? VideoId { get; init; }

    /// <summary>
    /// Заголовки глав видео (parallel array с <see cref="ChapterTimestamps"/>).
    /// Только для VIDEO-материалов; для остальных типов пустой.
    /// </summary>
    public IReadOnlyList<string> ChapterTitles { get; init; } = [];

    /// <summary>
    /// Offset'ы глав в секундах (parallel array с <see cref="ChapterTitles"/>).
    /// Только для VIDEO-материалов; для остальных типов пустой.
    /// </summary>
    public IReadOnlyList<int> ChapterTimestamps { get; init; } = [];

    /// <summary>
    /// True когда материал привязан хотя бы к одному курсу, но НИ один из этих
    /// курсов не PUBLISHED (все архивированы/draft) — search прячет документ (#378).
    /// False для never-bound orphan'а (0 привязок) — остаётся видимым (#77).
    /// Только для материалов; у прочих entity_type всегда false.
    /// </summary>
    public bool IsCourseOrphaned { get; init; }
}
