using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ProgressService.Domain.LevelTests;

namespace ProgressService.Infrastructure.Postgres.Configuration;

/// <summary>
///     Сериализация JSONB-колонок <c>level_test_attempts</c> ↔ доменные типы level-test'а.
///     Явные value converters — зеркало <c>QuizAttemptAnswersJson</c> (НЕ OwnsMany+ToJson:
///     у JSON-owned сущностей ключевые свойства не round-trip'ятся). Ключи JSON — PascalCase.
///     <c>question_results</c>/<c>section_scores</c>/<c>grading_config</c> — доменные records,
///     STJ биндит их по позиционному конструктору напрямую. Issue #479.
/// </summary>
public static class LevelTestAttemptJson
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.General);

    public static readonly ValueConverter<IReadOnlyList<LevelTestAnswer>, string> AnswersConverter =
        new(
            answers => SerializeAnswers(answers),
            json => DeserializeAnswers(json));

    /// <summary>Ответы пишутся один раз при сабмите и не мутируются — сравнение по снапшоту.</summary>
    public static readonly ValueComparer<IReadOnlyList<LevelTestAnswer>> AnswersComparer =
        new(
            (a, b) => SerializeAnswers(a!) == SerializeAnswers(b!),
            v => SerializeAnswers(v).GetHashCode(StringComparison.Ordinal),
            v => DeserializeAnswers(SerializeAnswers(v)));

    public static readonly ValueConverter<IReadOnlyList<LevelTestQuestionResult>, string> QuestionResultsConverter =
        new(
            results => Serialize(results),
            json => DeserializeList<LevelTestQuestionResult>(json));

    /// <summary>
    ///     QuestionResults заменяются целиком при AI-грейде (<c>ApplyAiGrades</c>) —
    ///     снапшот-сравнение ловит и замену списка, и поэлементные отличия.
    /// </summary>
    public static readonly ValueComparer<IReadOnlyList<LevelTestQuestionResult>> QuestionResultsComparer =
        new(
            (a, b) => Serialize(a!) == Serialize(b!),
            v => Serialize(v).GetHashCode(StringComparison.Ordinal),
            v => DeserializeList<LevelTestQuestionResult>(Serialize(v)));

    public static readonly ValueConverter<IReadOnlyList<LevelTestSectionScore>, string> SectionScoresConverter =
        new(
            sections => Serialize(sections),
            json => DeserializeList<LevelTestSectionScore>(json));

    public static readonly ValueComparer<IReadOnlyList<LevelTestSectionScore>> SectionScoresComparer =
        new(
            (a, b) => Serialize(a!) == Serialize(b!),
            v => Serialize(v).GetHashCode(StringComparison.Ordinal),
            v => DeserializeList<LevelTestSectionScore>(Serialize(v)));

    public static readonly ValueConverter<LevelTestGradingConfig, string> GradingConfigConverter =
        new(
            config => Serialize(config),
            json => DeserializeGradingConfig(json));

    public static readonly ValueComparer<LevelTestGradingConfig> GradingConfigComparer =
        new(
            (a, b) => Serialize(a!) == Serialize(b!),
            v => Serialize(v).GetHashCode(StringComparison.Ordinal),
            v => DeserializeGradingConfig(Serialize(v)));

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, _jsonOptions);

    private static IReadOnlyList<T> DeserializeList<T>(string json)
    {
        List<T>? payload = JsonSerializer.Deserialize<List<T>>(json, _jsonOptions);
        return payload is null || payload.Count == 0 ? [] : payload;
    }

    private static LevelTestGradingConfig DeserializeGradingConfig(string json)
    {
        // Mirror-DTO с nullable-полями: переживает '{}' (DB default) без NRE.
        GradingConfigJson? payload = JsonSerializer.Deserialize<GradingConfigJson>(json, _jsonOptions);
        if (payload is null)
        {
            return new LevelTestGradingConfig([], null);
        }

        return new LevelTestGradingConfig(
            (payload.Thresholds ?? [])
                .Select(t => new LevelTestLevelThreshold(t.Level, t.MinPercent))
                .ToList(),
            payload.FallbackCourseId);
    }

    private static string SerializeAnswers(IReadOnlyList<LevelTestAnswer> answers)
    {
        List<AnswerJson> payload = answers
            .Select(a => new AnswerJson(a.QuestionId, a.SelectedOptionIds.ToList(), a.TextAnswer))
            .ToList();

        return JsonSerializer.Serialize(payload, _jsonOptions);
    }

    private static IReadOnlyList<LevelTestAnswer> DeserializeAnswers(string json)
    {
        List<AnswerJson>? payload = JsonSerializer.Deserialize<List<AnswerJson>>(json, _jsonOptions);
        if (payload is null || payload.Count == 0)
            return [];

        return payload
            .Select(a => LevelTestAnswer.Create(a.QuestionId, a.SelectedOptionIds, a.TextAnswer).Value)
            .ToList();
    }

    private sealed record AnswerJson(
        Guid QuestionId,
        List<Guid>? SelectedOptionIds,
        string? TextAnswer);

    private sealed record GradingConfigJson(
        List<ThresholdJson>? Thresholds,
        Guid? FallbackCourseId);

    private sealed record ThresholdJson(string Level, int MinPercent);
}
