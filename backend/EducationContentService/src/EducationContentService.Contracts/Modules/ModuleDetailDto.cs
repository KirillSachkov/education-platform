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
    string? CoverUrl = null,
    int? QuestionsCount = null,
    Guid? QuizId = null);