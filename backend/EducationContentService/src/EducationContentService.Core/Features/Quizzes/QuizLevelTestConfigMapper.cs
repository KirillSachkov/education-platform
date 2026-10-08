using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;

namespace EducationContentService.Core.Features.Quizzes;

/// <summary>
///     Маппинг request-DTO → domain VO для level-test конфигурации квиза.
///     Доменные инварианты (непустые пороги, проценты 0..100, уникальные ключи секций)
///     живут в фабрике <see cref="LevelTestConfig"/> — мапер только парсит уровни,
///     возвращая первую доменную ошибку.
/// </summary>
public static class QuizLevelTestConfigMapper
{
    public static Result<LevelTestConfig?, Error> Map(LevelTestConfigRequest? request)
    {
        if (request is null)
            return Result.Success<LevelTestConfig?, Error>(null);

        var thresholds = new List<LevelThreshold>(request.LevelThresholds?.Count ?? 0);
        foreach (LevelThresholdRequest threshold in request.LevelThresholds ?? [])
        {
            if (!Enum.TryParse(threshold.Level, ignoreCase: true, out DeveloperLevel level))
                return EducationErrors.QuizLevelTestConfigInvalid($"неизвестный уровень порога: {threshold.Level}");

            thresholds.Add(new LevelThreshold(level, threshold.MinPercent));
        }

        List<LevelTestSection> sections = (request.Sections ?? [])
            .Select(s => new LevelTestSection(s.Key, s.Title, s.Weight, s.RecommendedCourseId))
            .ToList();

        Result<LevelTestConfig, Error> configResult = LevelTestConfig.Create(
            thresholds, sections, request.FallbackCourseId);
        if (configResult.IsFailure)
            return configResult.Error;

        return Result.Success<LevelTestConfig?, Error>(configResult.Value);
    }
}
