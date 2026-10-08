using TrainerService.Contracts.Questions;
using TrainerService.Domain;
using TrainerService.Domain.Questions;

namespace TrainerService.Core.Features.Questions.UseCases;

/// <summary>
///     Shared parsing/mapping for admin question CRUD (#623): question type + difficulty strings →
///     enums, and option DTOs → the domain factory's <c>(Text, IsCorrect)</c> tuples.
/// </summary>
internal static class QuestionInputParser
{
    public static Result<TrainerQuestionType, Error> ParseType(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && Enum.TryParse(raw.Trim(), ignoreCase: false, out TrainerQuestionType parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        return TrainerServiceErrors.Question.InvalidType(raw ?? string.Empty);
    }

    public static Result<QuestionDifficulty?, Error> ParseDifficulty(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (QuestionDifficulty?)null;

        if (Enum.TryParse(raw.Trim(), ignoreCase: false, out QuestionDifficulty parsed) && Enum.IsDefined(parsed))
            return parsed;

        return TrainerServiceErrors.Bank.InvalidDifficulty(raw);
    }

    public static IReadOnlyList<(string Text, bool IsCorrect)> MapOptions(
        IReadOnlyList<QuestionOptionInputDto>? options) =>
        options is null
            ? []
            : options.Select(o => (o.Text, o.IsCorrect)).ToList();
}
