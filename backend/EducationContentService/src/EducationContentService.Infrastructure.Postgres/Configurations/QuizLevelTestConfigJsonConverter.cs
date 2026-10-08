using System.Text.Json;
using EducationContentService.Domain.Quizzes;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EducationContentService.Infrastructure.Postgres.Configurations;

/// <summary>
///     Сериализация <c>quizzes.level_test_config</c> (jsonb, nullable) ↔
///     <see cref="LevelTestConfig"/>. Тот же явный value-converter паттерн, что у
///     <see cref="QuizQuestionsJson"/> (НЕ OwnsOne+ToJson). Ключи JSON — camelCase
///     (контракт эпика level-test #476): <c>levelThresholds[].level/minPercent</c>,
///     <c>sections[].key/title/weight/recommendedCourseId</c>, <c>fallbackCourseId</c>;
///     уровни — строками UPPER_SNAKE_CASE («JUNIOR»/«MIDDLE»/«SENIOR»).
///     NULL-значение колонки EF обрабатывает сам — конвертер null не получает.
///     Чтение валидирует через доменную фабрику (<c>.Value</c> бросает на битых данных).
/// </summary>
public static class QuizLevelTestConfigJson
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.General)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static readonly ValueConverter<LevelTestConfig?, string?> Converter =
        new(
            config => config == null ? null : Serialize(config),
            json => json == null ? null : Deserialize(json));

    /// <summary>
    ///     Сравнение по сериализованному снапшоту: VO immutable и заменяется целиком
    ///     (<see cref="Quiz.Update"/>) — JSON-сравнение корректно ловит replace.
    /// </summary>
    public static readonly ValueComparer<LevelTestConfig?> Comparer =
        new(
            (a, b) => a == null ? b == null : b != null && Serialize(a) == Serialize(b),
            v => v == null ? 0 : Serialize(v).GetHashCode(StringComparison.Ordinal),
            v => v == null ? null : Deserialize(Serialize(v)));

    private static string Serialize(LevelTestConfig config)
    {
        var payload = new ConfigJson(
            config.LevelThresholds
                .Select(t => new ThresholdJson(t.Level.ToString(), t.MinPercent))
                .ToList(),
            config.Sections
                .Select(s => new SectionJson(s.Key, s.Title, s.Weight, s.RecommendedCourseId))
                .ToList(),
            config.FallbackCourseId);

        return JsonSerializer.Serialize(payload, _jsonOptions);
    }

    private static LevelTestConfig Deserialize(string json)
    {
        ConfigJson payload = JsonSerializer.Deserialize<ConfigJson>(json, _jsonOptions)!;

        List<LevelThreshold> thresholds = (payload.LevelThresholds ?? [])
            .Select(t => new LevelThreshold(
                Enum.Parse<DeveloperLevel>(t.Level, ignoreCase: true),
                t.MinPercent))
            .ToList();

        List<LevelTestSection> sections = (payload.Sections ?? [])
            .Select(s => new LevelTestSection(s.Key, s.Title, s.Weight, s.RecommendedCourseId))
            .ToList();

        return LevelTestConfig.Create(thresholds, sections, payload.FallbackCourseId).Value;
    }

    private sealed record ConfigJson(
        List<ThresholdJson>? LevelThresholds,
        List<SectionJson>? Sections,
        Guid? FallbackCourseId);

    private sealed record ThresholdJson(string Level, int MinPercent);

    private sealed record SectionJson(string Key, string Title, decimal Weight, Guid? RecommendedCourseId);
}
