using EducationContentService.Contracts.Quizzes;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Quizzes;

namespace ProgressService.Core.Features.QuizAttempts;

/// <summary>
///     Итог автогрейдинга попытки против answer-key: балл, зачёт и per-вопрос разбор
///     в порядке вопросов квиза.
/// </summary>
public sealed record QuizAttemptGrading(
    int ScorePercent,
    bool Passed,
    IReadOnlyList<QuizAttemptQuestionResultResponse> Questions);

/// <summary>
///     Детерминированный автогрейдер попыток квиза против S2S answer-key из ECS.
///     Семантика:
///     <list type="bullet">
///         <item>SINGLE_CHOICE / MULTI_CHOICE — верно ⇔ множество выбранных вариантов
///             В ТОЧНОСТИ совпадает с множеством правильных (строго, без partial credit;
///             неотвеченный вопрос — неверно).</item>
///         <item>OPEN_TEXT (и неизвестные будущие типы) — не автогрейдится:
///             <c>Correct=null</c>, в знаменатель балла не входит — самопроверка по
///             эталонному ответу.</item>
///         <item>Квиз без choice-вопросов — self-check-only:
///             <see cref="SELF_CHECK_ONLY_SCORE_PERCENT"/> и автоматический зачёт.</item>
///     </list>
///     Балл = round(100 × верных / всех choice-вопросов), округление от половины вверх.
/// </summary>
public static class QuizAttemptGrader
{
    private static string NormalizeExactAnswer(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    /// <summary>Балл квиза из одних OPEN_TEXT-вопросов: грейдить нечего, прохождение засчитывается.</summary>
    public const int SELF_CHECK_ONLY_SCORE_PERCENT = 100;

    private const string SINGLE_CHOICE = "SINGLE_CHOICE";
    private const string MULTI_CHOICE = "MULTI_CHOICE";
    private const string EXACT_TEXT = "EXACT_TEXT";

    public static QuizAttemptGrading Grade(
        QuizAnswerKeyDto answerKey,
        IReadOnlyList<QuizAttemptAnswer> answers)
    {
        Dictionary<Guid, QuizAttemptAnswer> answersByQuestionId = answers.ToDictionary(a => a.QuestionId);

        int totalChoiceQuestions = 0;
        int correctChoiceQuestions = 0;
        var questions = new List<QuizAttemptQuestionResultResponse>(answerKey.Questions.Count);

        foreach (QuizAnswerKeyQuestionDto question in answerKey.Questions)
        {
            QuizAttemptAnswer? answer = answersByQuestionId.GetValueOrDefault(question.Id);
            IReadOnlyList<Guid> selectedOptionIds = answer?.SelectedOptionIds ?? [];

            bool? correct = null;
            if (IsAutoGradableChoice(question.Type))
            {
                totalChoiceQuestions++;
                correct = selectedOptionIds.ToHashSet().SetEquals(question.CorrectOptionIds);
                if (correct.Value)
                {
                    correctChoiceQuestions++;
                }
            }
            else if (string.Equals(question.Type, EXACT_TEXT, StringComparison.Ordinal))
            {
                // Рукописный точный ответ (#528) — детерминированный, входит в балл как choice.
                totalChoiceQuestions++;
                correct = answer?.TextAnswer is not null
                    && question.ReferenceAnswer is not null
                    && string.Equals(
                        NormalizeExactAnswer(answer.TextAnswer),
                        NormalizeExactAnswer(question.ReferenceAnswer),
                        StringComparison.Ordinal);
                if (correct.Value)
                {
                    correctChoiceQuestions++;
                }
            }

            IReadOnlyList<QuizAttemptOptionResultResponse> optionResults =
                (question.Options ?? [])
                    .Select(o => new QuizAttemptOptionResultResponse(o.Id, o.Text))
                    .ToList();

            questions.Add(new QuizAttemptQuestionResultResponse(
                question.Id,
                question.Type,
                correct,
                selectedOptionIds,
                question.CorrectOptionIds,
                answer?.TextAnswer,
                question.ReferenceAnswer,
                optionResults,
                question.Explanation));
        }

        int scorePercent = totalChoiceQuestions == 0
            ? SELF_CHECK_ONLY_SCORE_PERCENT
            : (int)Math.Round(
                100.0 * correctChoiceQuestions / totalChoiceQuestions,
                MidpointRounding.AwayFromZero);

        bool passed = scorePercent >= answerKey.PassingScorePercent;

        return new QuizAttemptGrading(scorePercent, passed, questions);
    }

    /// <summary>
    ///     Собирает контрактный результат по сохранённой попытке: балл/зачёт — персистентный
    ///     факт на момент сабмита, per-вопрос разбор пересчитывается против актуального
    ///     answer-key (правка квиза автором меняет разбор, но не исторический балл).
    /// </summary>
    public static QuizAttemptResultResponse BuildResult(QuizAttempt attempt, QuizAnswerKeyDto answerKey)
    {
        QuizAttemptGrading grading = Grade(answerKey, attempt.Answers);

        return new QuizAttemptResultResponse(
            attempt.Id,
            attempt.ScorePercent,
            attempt.Passed,
            answerKey.PassingScorePercent,
            attempt.SubmittedAt,
            grading.Questions);
    }

    /// <summary>
    ///     Грейдит ОДИН вопрос ровно по той же семантике, что и <see cref="Grade"/>:
    ///     SINGLE/MULTI_CHOICE — выбранное множество = правильному (строго, без partial
    ///     credit); EXACT_TEXT — нормализованное сравнение с эталоном; OPEN_TEXT (и прочие
    ///     типы) — не автогрейдится (<c>null</c>). Используется per-question check-эндпоинтом
    ///     (#556) для немедленной обратной связи без сохранения попытки.
    /// </summary>
    public static bool? GradeOne(
        QuizAnswerKeyQuestionDto question,
        IReadOnlyList<Guid>? selectedOptionIds,
        string? textAnswer)
    {
        if (IsAutoGradableChoice(question.Type))
        {
            return (selectedOptionIds ?? []).ToHashSet().SetEquals(question.CorrectOptionIds);
        }

        if (string.Equals(question.Type, EXACT_TEXT, StringComparison.Ordinal))
        {
            return textAnswer is not null
                && question.ReferenceAnswer is not null
                && string.Equals(
                    NormalizeExactAnswer(textAnswer),
                    NormalizeExactAnswer(question.ReferenceAnswer),
                    StringComparison.Ordinal);
        }

        return null;
    }

    private static bool IsAutoGradableChoice(string questionType) =>
        string.Equals(questionType, SINGLE_CHOICE, StringComparison.Ordinal)
        || string.Equals(questionType, MULTI_CHOICE, StringComparison.Ordinal);
}