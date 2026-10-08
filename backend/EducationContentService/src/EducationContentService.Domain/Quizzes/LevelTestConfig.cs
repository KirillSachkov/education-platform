namespace EducationContentService.Domain.Quizzes;

/// <summary>Порог уровня: минимальный процент правильных ответов для присвоения уровня.</summary>
public sealed record LevelThreshold(DeveloperLevel Level, int MinPercent);

/// <summary>
///     Секция теста уровня — тематический блок вопросов. <paramref name="Key"/> —
///     kebab-case ключ (например, <c>csharp-basics</c>), матчится с
///     <see cref="QuizQuestion.Section"/>; <paramref name="RecommendedCourseId"/> —
///     курс-рекомендация при слабом результате по секции.
/// </summary>
public sealed record LevelTestSection(string Key, string Title, decimal Weight, Guid? RecommendedCourseId);

/// <summary>
///     Value Object — конфигурация level-test квиза (<see cref="Quiz.Purpose"/> =
///     <see cref="QuizPurpose.LEVEL_TEST"/>): пороги уровней, секции с весами и
///     курсами-рекомендациями, fallback-курс. Хранится JSONB-документом
///     <c>quizzes.level_test_config</c> (см. QuizLevelTestConfigJson).
/// </summary>
public sealed class LevelTestConfig
{
    private LevelTestConfig(
        IReadOnlyList<LevelThreshold> levelThresholds,
        IReadOnlyList<LevelTestSection> sections,
        Guid? fallbackCourseId)
    {
        LevelThresholds = levelThresholds;
        Sections = sections;
        FallbackCourseId = fallbackCourseId;
    }

    /// <summary>Пороги уровней (непустой набор; minPercent 0..100).</summary>
    public IReadOnlyList<LevelThreshold> LevelThresholds { get; }

    /// <summary>Секции теста (ключи уникальны; допустим пустой набор).</summary>
    public IReadOnlyList<LevelTestSection> Sections { get; }

    /// <summary>Курс-рекомендация по умолчанию, если секционной рекомендации нет.</summary>
    public Guid? FallbackCourseId { get; }

    public static Result<LevelTestConfig, Error> Create(
        IReadOnlyList<LevelThreshold>? levelThresholds,
        IReadOnlyList<LevelTestSection>? sections,
        Guid? fallbackCourseId)
    {
        if (levelThresholds is null || levelThresholds.Count == 0)
            return EducationErrors.QuizLevelTestConfigInvalid("пороги уровней не заданы");

        if (levelThresholds.Any(t => t.MinPercent is < 0 or > 100))
            return EducationErrors.QuizLevelTestConfigInvalid("минимальный процент порога должен быть от 0 до 100");

        var normalizedSections = new List<LevelTestSection>(sections?.Count ?? 0);
        foreach (LevelTestSection section in sections ?? [])
        {
            if (string.IsNullOrWhiteSpace(section.Key))
                return EducationErrors.QuizLevelTestConfigInvalid("у секции отсутствует ключ");

            if (string.IsNullOrWhiteSpace(section.Title))
                return EducationErrors.QuizLevelTestConfigInvalid($"у секции '{section.Key}' отсутствует название");

            if (section.Weight <= 0)
                return EducationErrors.QuizLevelTestConfigInvalid($"вес секции '{section.Key}' должен быть положительным");

            normalizedSections.Add(section with { Key = section.Key.Trim(), Title = section.Title.Trim() });
        }

        if (normalizedSections.Select(s => s.Key).Distinct(StringComparer.Ordinal).Count() != normalizedSections.Count)
            return EducationErrors.QuizLevelTestConfigInvalid("ключи секций должны быть уникальными");

        return new LevelTestConfig([.. levelThresholds], normalizedSections, fallbackCourseId);
    }
}
