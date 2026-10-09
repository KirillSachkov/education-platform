using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain.Quizzes;

namespace EducationContentService.Core.Features.Quizzes;

/// <summary>
///     Проекции агрегата <see cref="Quiz"/> в контрактные DTO. Студенческая проекция
///     намеренно строится из типа без полей CorrectOptionIds/ReferenceAnswer — ответы
///     физически не попадают в JSON студенческого эндпоинта.
/// </summary>
public static class QuizDtoMapper
{
    public static QuizAuthorDto ToAuthorDto(Quiz quiz) =>
        new(
            quiz.Id,
            quiz.AuthorId,
            quiz.Title.Value,
            quiz.Status.ToString(),
            quiz.AccessType.ToString(),
            quiz.Purpose.ToString(),
            quiz.PassingScorePercent,
            quiz.Questions
                .Select(q => new QuizQuestionAuthorDto(
                    q.Id,
                    q.Type.ToString(),
                    q.Text,
                    q.Section,
                    q.Difficulty?.ToString(),
                    q.Options.Select(o => new QuizOptionDto(o.Id, o.Text)).ToList(),
                    q.CorrectOptionIds,
                    q.ReferenceAnswer,
                    q.Explanation))
                .ToList(),
            quiz.CreatedAt,
            quiz.UpdatedAt);

    /// <summary>Студенческая проекция квиза без ключа ответов.</summary>
    /// <param name="materialId">
    ///     Материал-контекст запроса (<c>GET /materials/{id}/quiz</c>) либо <c>null</c>
    ///     для standalone-чтения <c>GET /quizzes/{id}/student</c> (#490).
    /// </param>
    public static QuizStudentDto ToStudentDto(Quiz quiz, Guid? materialId) =>
        new(
            quiz.Id,
            materialId,
            quiz.Title.Value,
            quiz.PassingScorePercent,
            quiz.Questions
                .Select(q => new QuizQuestionStudentDto(
                    q.Id,
                    q.Type.ToString(),
                    q.Text,
                    q.Section,
                    q.Difficulty?.ToString(),
                    q.Options.Select(o => new QuizOptionDto(o.Id, o.Text)).ToList()))
                .ToList());

    public static QuizAnswerKeyDto ToAnswerKeyDto(Quiz quiz) =>
        new(
            quiz.Id,
            quiz.Purpose.ToString(),
            quiz.PassingScorePercent,
            quiz.Questions
                .Select(q => new QuizAnswerKeyQuestionDto(
                    q.Id,
                    q.Type.ToString(),
                    q.Text,
                    q.Section,
                    q.Difficulty?.ToString(),
                    q.CorrectOptionIds,
                    q.ReferenceAnswer,
                    q.Options.Select(o => new QuizOptionDto(o.Id, o.Text)).ToList(),
                    q.Explanation))
                .ToList(),
            quiz.AccessType.ToString());
}