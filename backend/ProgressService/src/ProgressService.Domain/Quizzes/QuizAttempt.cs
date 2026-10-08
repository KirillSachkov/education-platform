using ProgressService.Domain.Quizzes.Events;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Quizzes;

/// <summary>
///     Попытка прохождения квиза. User-scoped, попыток на пару (UserId, QuizId)
///     может быть сколько угодно — уникального индекса нет намеренно (best/last
///     вычисляются на чтении). Score/Passed — зафиксированный факт на момент сабмита:
///     правка квиза автором задним числом их не меняет. Попытка с <c>Passed=true</c>
///     поднимает <see cref="QuizAttemptPassedEvent"/> — каскад на module_item_progress
///     (ST-13 #493). Issue #470.
/// </summary>
public sealed class QuizAttempt : AggregateRoot
{
    public const int MAX_SCORE_PERCENT = 100;

    private QuizAttempt(
        Guid userId,
        Guid quizId,
        IReadOnlyList<QuizAttemptAnswer> answers,
        int scorePercent,
        bool passed)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        QuizId = quizId;
        Answers = answers;
        ScorePercent = scorePercent;
        Passed = passed;
        SubmittedAt = DateTime.UtcNow;
    }

    private QuizAttempt()
    {
        Answers = [];
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid QuizId { get; private set; }

    /// <summary>
    ///     Vestigial (ST-13 #493): снапшот материала из MVP-модели «квиз гейтится по
    ///     материалу». Квиз standalone — для новых попыток всегда NULL, колонка
    ///     останется до отдельной cleanup-миграции (не в этом эпике).
    /// </summary>
    public Guid? MaterialId { get; private set; }

    public IReadOnlyList<QuizAttemptAnswer> Answers { get; private set; }

    public int ScorePercent { get; private set; }

    public bool Passed { get; private set; }

    public DateTime SubmittedAt { get; private set; }

    public static Result<QuizAttempt, Error> Create(
        Guid userId,
        Guid quizId,
        IReadOnlyList<QuizAttemptAnswer> answers,
        int scorePercent,
        bool passed)
    {
        if (userId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(userId));
        }

        if (quizId == Guid.Empty)
        {
            return GeneralErrors.ValueIsInvalid(nameof(quizId));
        }

        if (scorePercent is < 0 or > MAX_SCORE_PERCENT)
        {
            return GeneralErrors.ValueIsInvalid(nameof(scorePercent));
        }

        var attempt = new QuizAttempt(userId, quizId, answers, scorePercent, passed);

        if (passed)
        {
            // На каждой passed-попытке (включая повторные) — handler идемпотентен,
            // а квиз мог быть привязан к новому курсу между попытками.
            attempt.RaiseDomainEvent(new QuizAttemptPassedEvent(userId, quizId));
        }

        return attempt;
    }
}
