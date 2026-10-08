using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProgressService.Domain.Quizzes;

namespace ProgressService.Infrastructure.Postgres.Configuration;

/// <summary>
///     Сериализация <c>quiz_attempts.answers</c> (jsonb) ↔ <see cref="QuizAttemptAnswer"/>[].
///     Явный value converter — зеркало <c>QuizQuestionsJson</c> в ECS (НЕ OwnsMany+ToJson:
///     у JSON-owned сущностей ключевые свойства не round-trip'ятся). Ключи JSON — PascalCase.
///     Чтение валидирует через доменную фабрику (<c>.Value</c> бросает на битых данных).
/// </summary>
public static class QuizAttemptAnswersJson
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.General);

    public static readonly ValueConverter<IReadOnlyList<QuizAttemptAnswer>, string> Converter =
        new(
            answers => Serialize(answers),
            json => Deserialize(json));

    /// <summary>
    ///     Сравнение по сериализованному снапшоту: элементы immutable, список ответов
    ///     пишется один раз при сабмите и не мутируется.
    /// </summary>
    public static readonly ValueComparer<IReadOnlyList<QuizAttemptAnswer>> Comparer =
        new(
            (a, b) => Serialize(a!) == Serialize(b!),
            v => Serialize(v).GetHashCode(StringComparison.Ordinal),
            v => Deserialize(Serialize(v)));

    private static string Serialize(IReadOnlyList<QuizAttemptAnswer> answers)
    {
        List<AnswerJson> payload = answers
            .Select(a => new AnswerJson(a.QuestionId, a.SelectedOptionIds.ToList(), a.TextAnswer))
            .ToList();

        return JsonSerializer.Serialize(payload, _jsonOptions);
    }

    private static IReadOnlyList<QuizAttemptAnswer> Deserialize(string json)
    {
        List<AnswerJson>? payload = JsonSerializer.Deserialize<List<AnswerJson>>(json, _jsonOptions);
        if (payload is null || payload.Count == 0)
            return [];

        return payload
            .Select(a => QuizAttemptAnswer.Create(a.QuestionId, a.SelectedOptionIds, a.TextAnswer).Value)
            .ToList();
    }

    private sealed record AnswerJson(
        Guid QuestionId,
        List<Guid>? SelectedOptionIds,
        string? TextAnswer);
}
