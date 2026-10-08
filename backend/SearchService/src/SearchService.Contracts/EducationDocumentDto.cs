using Common;

namespace SearchService.Contracts;

public sealed record EducationDocumentDto
{
    public required Guid EntityId { get; init; }

    public required EntityType EntityType { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public Guid? ImageId { get; init; }

    public Guid? CourseId { get; init; }

    public string? CourseSlug { get; init; }

    public string? CourseTitle { get; init; }

    /// <summary>
    /// AuthorId документа (для курсов — автор курса, для материалов — автор материала).
    /// Нужен фронту, чтобы адаптировать search-hit в feed-совместимый DTO в КБ.
    /// </summary>
    public Guid? AuthorId { get; init; }

    public Guid? ProjectId { get; init; }

    public string? ProjectTitle { get; init; }

    public Guid? ModuleId { get; init; }

    public string? ModuleTitle { get; init; }

    public IReadOnlyList<Guid> TagIds { get; init; } = [];

    public IReadOnlyList<string> TagTitles { get; init; } = [];

    public DateTime UpdatedAtUtc { get; init; }

    /// <summary>
    /// Доступен ли документ текущему пользователю. Для анонимов и не-зачисленных
    /// на курс с ENROLLED-контентом вернётся false; UI показывает замок + CTA.
    /// </summary>
    public bool IsAccessible { get; init; } = true;

    /// <summary>
    /// Код причины блокировки: <c>anonymous | trial_required | standard_required |
    /// not_enrolled</c>. Null, когда документ доступен. Стабильный API-контракт с фронтом.
    /// </summary>
    public string? LockReason { get; init; }

    /// <summary>
    /// Kind материала (ARTICLE / VIDEO / NOTE / STREAM). Только для EntityType.Material; для
    /// остальных типов — null. Используется фронтом для показа правильной иконки.
    /// </summary>
    public string? MaterialKind { get; init; }

    /// <summary>
    /// Kinescope-thumbnail для видео-материалов. Резолвится backend'ом в handler'е
    /// поиска через FileService batch, когда у материала нет кастомной обложки.
    /// Чтобы фронт мог показать реальный превью, а не gradient-заглушку.
    /// </summary>
    public string? VideoThumbnailUrl { get; init; }

    /// <summary>
    /// Заголовки глав видео (Kinescope chapters) — нужны фронту чтобы показать
    /// конкретный сниппет совпавшей главы и собрать deep-link. Параллельный массив
    /// с <see cref="ChapterTimestamps"/>: <c>ChapterTitles[i]</c> ↔ <c>ChapterTimestamps[i]</c>.
    /// Только для VIDEO-материалов; для остальных типов пустой.
    /// </summary>
    public IReadOnlyList<string> ChapterTitles { get; init; } = [];

    /// <summary>
    /// Offset'ы глав в секундах (parallel с <see cref="ChapterTitles"/>). По индексу
    /// совпавшей главы (<c>SearchHighlight.MatchedIndices</c>) фронт собирает
    /// <c>?t=&lt;seconds&gt;</c> для deep-link на нужную секунду в Kinescope-плеере.
    /// </summary>
    public IReadOnlyList<int> ChapterTimestamps { get; init; } = [];
}
