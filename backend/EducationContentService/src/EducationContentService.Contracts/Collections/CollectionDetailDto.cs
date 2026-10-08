using EducationContentService.Contracts.Materials;

namespace EducationContentService.Contracts.Collections;

public sealed record CollectionDetailDto(
    Guid Id,
    Guid AuthorId,
    string Title,
    string? Description,
    Guid? CoverImageId,
    string? CoverImageUrl,
    Guid? CourseId,
    string? CourseTitle,
    string? CourseSlug,
    string Status,
    string AccessType,
    bool IsAccessible,
    string? LockReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<CollectionSectionDto> Sections);

public sealed record CollectionSectionDto(
    Guid Id,
    string? Title,
    string? Description,
    IReadOnlyList<CollectionItemDto> Items);

/// <summary>
///     Элемент подборки — generic (#491): материал или квиз.
/// </summary>
/// <param name="ReferenceId">Id материала (<c>itemType=MATERIAL</c>) или квиза (<c>itemType=QUIZ</c>).</param>
/// <param name="ItemType"><c>MATERIAL</c> | <c>QUIZ</c>.</param>
/// <param name="Material">Карточка материала; <c>null</c> для QUIZ-items.</param>
/// <param name="QuizTitle">Заголовок квиза; <c>null</c> для MATERIAL-items.</param>
/// <param name="QuestionsCount">Число вопросов квиза; <c>null</c> для MATERIAL-items.</param>
public sealed record CollectionItemDto(
    Guid Id,
    Guid ReferenceId,
    string ItemType,
    MaterialSummaryDto? Material,
    string? QuizTitle,
    int? QuestionsCount,
    bool IsAccessible,
    string? LockReason);
