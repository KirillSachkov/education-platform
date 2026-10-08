using ProgressService.Contracts.Responses;
using ProgressService.Domain.LevelTests;

namespace ProgressService.Core.Features.LevelTests;

/// <summary>
///     Маппинг попытки level-test'а в контрактные DTO. Lead-gate реализован отдельными
///     типами: тизер (аноним / неклеймленная попытка) физически не содержит секций и
///     per-вопрос разбора; полный результат — для владельца/админа. Issue #479.
/// </summary>
public static class LevelTestResultMapper
{
    public static LevelTestAttemptTeaserResponse BuildTeaser(LevelTestAttempt attempt) =>
        new(
            attempt.Id,
            attempt.OverallPercent,
            attempt.Level,
            TotalQuestions: attempt.QuestionResults.Count,
            AnsweredCount: attempt.Answers.Count(a => a.HasContent),
            attempt.AiGradingStatus.ToString());

    public static LevelTestAttemptResultResponse BuildFullResult(LevelTestAttempt attempt)
    {
        // Выбор пользователя хранится в answers, а разбор вопроса в question_results.
        // Сопоставляем их по идентификатору вопроса для самодостаточного разбора (#561).
        Dictionary<Guid, LevelTestAnswer> answersByQuestionId =
            attempt.Answers.ToDictionary(a => a.QuestionId);

        return new LevelTestAttemptResultResponse(
            attempt.Id,
            attempt.QuizId,
            attempt.OverallPercent,
            attempt.Level,
            attempt.AiGradingStatus.ToString(),
            attempt.RecommendedCourseId,
            attempt.SectionScores
                .Select(s => new LevelTestSectionScoreResponse(
                    s.Key,
                    s.Title,
                    s.Percent,
                    s.Level,
                    s.EarnedPoints,
                    s.MaxPoints))
                .ToList(),
            attempt.QuestionResults
                .Select(q => new LevelTestQuestionResultResponse(
                    q.QuestionId,
                    q.Section,
                    q.Difficulty,
                    q.Type,
                    q.IsCorrect,
                    q.PendingAi,
                    q.AiScore,
                    q.AiFeedback,
                    q.EarnedPoints,
                    q.MaxPoints,
                    (q.Options ?? [])
                        .Select(o => new LevelTestOptionResultResponse(o.Id, o.Text))
                        .ToList(),
                    q.CorrectOptionIds ?? [],
                    answersByQuestionId.GetValueOrDefault(q.QuestionId)?.SelectedOptionIds ?? [],
                    answersByQuestionId.GetValueOrDefault(q.QuestionId)?.TextAnswer,
                    q.ReferenceAnswer,
                    q.Explanation))
                .ToList(),
            BuildWeakestSectionKeys(attempt.SectionScores));
    }

    /// <summary>
    ///     Ключи самых слабых секций (минимальный Percent среди weight&gt;0 и MaxPoints&gt;0;
    ///     при равенстве — все). Питает CTA «подтянуть слабое место» на фронте (ST-6).
    /// </summary>
    private static IReadOnlyList<string> BuildWeakestSectionKeys(
        IReadOnlyList<LevelTestSectionScore> sections)
    {
        LevelTestSectionScore? weakest = LevelTestScoring.FindWeakestSection(sections);
        if (weakest is null)
        {
            return [];
        }

        return sections
            .Where(s => s.Weight > 0m && s.MaxPoints > 0m && s.Percent == weakest.Percent)
            .Select(s => s.Key)
            .ToList();
    }
}
