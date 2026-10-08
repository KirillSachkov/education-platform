namespace EducationContentService.Contracts.Modules;

/// <summary>
///     Полная информация о модуле с перечнем элементов.
/// </summary>
public sealed record ModuleDetailDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string? Description,
    string? DetailedDescription,
    Guid? CourseId,
    string Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ModuleItemDto> Items);

/// <summary>
///     Элемент модуля (урок, статья, тест или задача).
///     Поля <see cref="HasTranscript"/>, <see cref="HasTimecodes"/>, <see cref="HasSummary"/>
///     заполняются только в course-builder context'е и только для item с
///     <c>ItemType=Material</c> и <c>Material.Kind=Video</c>. В остальных случаях — null.
///     Их используют автор-фронт-карточки модулей чтобы рисовать иконки артефактов
///     рядом с заголовком видео без лишних round-trip'ов в MaterialProcessingService.
///     <see cref="CoverUrl"/> заполняется в course-builder для material-item'ов:
///     manual cover (ImageId) → fallback на Kinescope thumbnail. На list-эндпоинте модуля
///     (<c>GetModuleDetail</c>) — null.
///     <see cref="QuestionsCount"/> / <see cref="QuizId"/> заполняются в course-builder
///     только для quiz-item'ов (<c>ItemType=Quiz</c>, ST-12 #492) — бейдж «N вопросов»
///     и переход в редактор квиза.
/// </summary>
public sealed record ModuleItemDto(
    Guid Id,
    Guid ReferenceId,
    string ItemType,
    string SortKey,
    bool IsOptional,
    string ViewPriority,
    string? Title,
    string? Status,
    string? AccessType,
    bool? HasTranscript = null,
    bool? HasTimecodes = null,
    bool? HasSummary = null,
    string? CoverUrl = null,
    int? QuestionsCount = null,
    Guid? QuizId = null);
