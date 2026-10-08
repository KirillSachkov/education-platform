namespace EducationContentService.Contracts.Materials;

/// <summary>
///     Краткая карточка материала для списков (таймлайн автора, каталог, пр.).
/// </summary>
/// <param name="QuizId">Квиз «Проверь себя» материала (<c>materials.quiz_id</c>, #489). <c>null</c> — нет квиза.</param>
public sealed record MaterialSummaryDto(
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
    Guid? QuizId = null)
{
    /// <summary>Превью обложки или кадр видео; заполняется handler-ом списка после батч-загрузки из FileService.</summary>
    public string? ThumbnailUrl { get; init; }

    /// <summary>
    ///     Курсы, к которым материал привязан через <c>course_materials</c>. Используется в picker'е
    ///     модуля/коллекции, чтобы автор видел, откуда переиспользуется материал. Пустой список —
    ///     материал в «базе знаний» (orphan). Заполняется только в scope=mine; для других scope'ов
    ///     возвращается пустой массив (не светим топологию чужих материалов).
    /// </summary>
    public IReadOnlyList<MaterialCourseBindingDto> Courses { get; init; } = [];

    /// <summary>
    ///     Уникальные просмотры (auth + anon). Обогащается списковыми handler'ами через
    ///     <c>IProgressServiceClient</c>. Issue #234.
    /// </summary>
    public long ViewsCount { get; init; }

    /// <summary>
    ///     Длительность привязанного видео в секундах (Kinescope). null — материал без видео
    ///     или метаданные ещё не готовы. Берётся из того же FileService batch'а, что и
    ///     thumbnail. Issue #500.
    /// </summary>
    public double? DurationSeconds { get; init; }
}
