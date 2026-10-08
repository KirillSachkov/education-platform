using System.Text.Json;
using System.Text.Json.Serialization;
using EducationContentService.Domain.Quizzes;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

/// <summary>
///     Сериализация <c>quizzes.questions</c> (jsonb) ↔ <see cref="QuizQuestion"/>[].
///     Явный value converter вместо OwnsMany+ToJson: у JSON-owned сущностей EF Core
///     свойство <c>Id</c> становится ключом и его значения не персистятся в документ —
///     а стабильные id вопросов/вариантов обязательны для грейдинга (ST-I). Ключи JSON —
///     PascalCase (консистентно с <c>issues.internal_materials</c> из EF ToJson());
///     исключение — level-test поля <c>section</c>/<c>difficulty</c> (#476): camelCase
///     по контракту эпика (см. spec ST-1), закреплено JsonPropertyName-атрибутами.
///     Чтение валидирует через доменные фабрики (<c>.Value</c> бросает на битых данных —
///     тот же паттерн, что у Title-конверсии).
/// </summary>
public static class QuizQuestionsJson
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.General);

    public static readonly ValueConverter<IReadOnlyList<QuizQuestion>, string> Converter =
        new(
            questions => Serialize(questions),
            json => Deserialize(json));

    /// <summary>
    ///     Сравнение по сериализованному снапшоту: элементы immutable, но список
    ///     заменяется целиком (<see cref="Quiz.UpdateQuestions"/>) — JSON-сравнение
    ///     корректно ловит и replace, и (на будущее) любую глубокую правку.
    /// </summary>
    public static readonly ValueComparer<IReadOnlyList<QuizQuestion>> Comparer =
        new(
            (a, b) => Serialize(a!) == Serialize(b!),
            v => Serialize(v).GetHashCode(StringComparison.Ordinal),
            v => Deserialize(Serialize(v)));

    private static string Serialize(IReadOnlyList<QuizQuestion> questions)
    {
        List<QuestionJson> payload = questions
            .Select(q => new QuestionJson(
                q.Id,
                q.Type.ToString(),
                q.Text,
                q.Options.Select(o => new OptionJson(o.Id, o.Text)).ToList(),
                q.CorrectOptionIds.ToList(),
                q.ReferenceAnswer,
                q.Section,
                q.Difficulty?.ToString(),
                q.Explanation))
            .ToList();

        return JsonSerializer.Serialize(payload, _jsonOptions);
    }

    private static IReadOnlyList<QuizQuestion> Deserialize(string json)
    {
        List<QuestionJson>? payload = JsonSerializer.Deserialize<List<QuestionJson>>(json, _jsonOptions);
        if (payload is null || payload.Count == 0)
            return [];

        var questions = new List<QuizQuestion>(payload.Count);
        foreach (QuestionJson q in payload)
        {
            List<QuizOption> options = (q.Options ?? [])
                .Select(o => QuizOption.Create(o.Id, o.Text).Value)
                .ToList();

            questions.Add(QuizQuestion.Create(
                q.Id,
                Enum.Parse<QuizQuestionType>(q.Type, ignoreCase: true),
                q.Text,
                options,
                q.CorrectOptionIds ?? [],
                q.ReferenceAnswer,
                q.Section,
                q.Difficulty is null ? null : Enum.Parse<QuestionDifficulty>(q.Difficulty, ignoreCase: true),
                q.Explanation).Value);
        }

        return questions;
    }

    private sealed record QuestionJson(
        Guid Id,
        string Type,
        string Text,
        List<OptionJson>? Options,
        List<Guid>? CorrectOptionIds,
        string? ReferenceAnswer,
        [property: JsonPropertyName("section")] string? Section = null,
        [property: JsonPropertyName("difficulty")] string? Difficulty = null,
        string? Explanation = null);

    private sealed record OptionJson(Guid Id, string Text);
}
