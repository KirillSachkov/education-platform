using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;

namespace EducationContentService.Core.Features.Quizzes;

/// <summary>
///     Маппинг request-DTO → domain value objects для Create/Update квиза.
///     Доменные инварианты (количество вариантов, правильные ответы, длины) живут
///     в фабриках <see cref="QuizQuestion"/>/<see cref="QuizOption"/> — мапер только
///     резолвит id и парсит тип, возвращая первую доменную ошибку.
/// </summary>
public static class QuizQuestionMapper
{
    public static Result<List<QuizQuestion>, Error> Map(IReadOnlyList<QuizQuestionRequest>? requests)
    {
        if (requests is null || requests.Count == 0)
            return new List<QuizQuestion>();

        var questions = new List<QuizQuestion>(requests.Count);

        foreach (QuizQuestionRequest request in requests)
        {
            if (!Enum.TryParse(request.Type, ignoreCase: true, out QuizQuestionType type))
                return EducationErrors.InvalidQuizQuestionType(request.Type);

            QuestionDifficulty? difficulty = null;
            if (!string.IsNullOrWhiteSpace(request.Difficulty))
            {
                if (!Enum.TryParse(request.Difficulty, ignoreCase: true, out QuestionDifficulty parsedDifficulty))
                    return EducationErrors.InvalidQuizQuestionDifficulty(request.Difficulty);

                difficulty = parsedDifficulty;
            }

            var options = new List<QuizOption>(request.Options?.Count ?? 0);
            foreach (QuizOptionRequest optionRequest in request.Options ?? [])
            {
                Result<QuizOption, Error> optionResult = QuizOption.Create(
                    optionRequest.Id is { } oid && oid != Guid.Empty ? oid : Guid.CreateVersion7(),
                    optionRequest.Text);
                if (optionResult.IsFailure)
                    return optionResult.Error;

                options.Add(optionResult.Value);
            }

            Result<QuizQuestion, Error> questionResult = QuizQuestion.Create(
                request.Id is { } qid && qid != Guid.Empty ? qid : Guid.CreateVersion7(),
                type,
                request.Text,
                options,
                request.CorrectOptionIds ?? [],
                request.ReferenceAnswer,
                request.Section,
                difficulty,
                request.Explanation);
            if (questionResult.IsFailure)
                return questionResult.Error;

            questions.Add(questionResult.Value);
        }

        return questions;
    }
}
