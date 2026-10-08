using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;

namespace TrainerService.Core.Database;

public interface IQuestionStudyStatesRepository
{
    Task AddAsync(QuestionStudyState state, CancellationToken ct = default);

    /// <summary>
    /// Состояние изучения вопроса вызывающим пользователем по unique-ключу (UserId, QuestionId).
    /// Нет строки → <see cref="TrainerServiceErrors.StudyState.NotFound"/> (caller обычно лениво создаёт).
    /// </summary>
    Task<Result<QuestionStudyState, Error>> GetByAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default);

    /// <summary>
    /// Список study-state'ов пользователя с фильтрами, прокинутыми в SQL (без array-overlap —
    /// Npgsql его не транслирует, #568). <paramref name="topicId"/> / <paramref name="status"/> —
    /// опциональные equality-фильтры; <paramref name="dueOnly"/>=true оставляет только
    /// <c>next_due_at &lt;= now</c> (SRS «на повтор сегодня»). Порядок: most-due first
    /// (<c>next_due_at ASC</c>), затем недавно виденные (<c>last_seen_at DESC</c>). <c>.Take(limit)</c>.
    /// </summary>
    Task<IReadOnlyList<QuestionStudyState>> GetManyForUserAsync(
        Guid userId,
        Guid? topicId,
        StudyStatus? status,
        bool dueOnly,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    ///     Study-state'ы пользователя по набору вопросов (для обогащения списка вопросов охвата
    ///     статусом). Фильтр <c>question_id = ANY(@ids)</c> через хранимый FK-столбец (equality,
    ///     не array-overlap по колонке — Npgsql его не транслирует, #568). Пустой набор → пустой список.
    /// </summary>
    Task<IReadOnlyList<QuestionStudyState>> GetForQuestionsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken ct = default);

    /// <summary>
    ///     Все study-state'ы пользователя (для per-topic аналитики «изучено / ошибок» в прогрессе).
    ///     Bounded в памяти потребителем по числу тем; на пользователя строк столько же, сколько он
    ///     реально касался вопросов.
    /// </summary>
    Task<IReadOnlyList<QuestionStudyState>> GetAllForUserAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    ///     «Мои ошибки»: study-state'ы пользователя со статусом WRONG или REVIEW (живой сигнал —
    ///     WRONG; REVIEW зарезервирован), опц. сужено по теме (equality). Most-recent-wrong first
    ///     (<c>last_seen_at DESC</c>), SQL-bounded по <paramref name="limit"/>. Сложность-фильтр —
    ///     не SQL (difficulty живёт в ECS, не в state), применяется потребителем после enrichment'а.
    /// </summary>
    Task<IReadOnlyList<QuestionStudyState>> GetMistakesForUserAsync(
        Guid userId,
        Guid? topicId,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    ///     SQL-side aggregation of the user's study-states for the stats summary
    ///     (<c>GET /trainer/stats/summary</c>, #568): per-status counts, total studied rows, SRS due-today
    ///     count, the 7-day due forecast, and retention sums — all computed in the DB (no row materialisation).
    /// </summary>
    Task<StudyStateAggregate> GetStudyStateAggregateForUserAsync(
        Guid userId,
        CancellationToken ct = default);
}

/// <summary>
///     SQL-side aggregation of a user's <see cref="QuestionStudyState"/> rows (#568 stats).
/// </summary>
/// <param name="StatusCounts">Per-<see cref="StudyStatus"/> row counts (only present statuses).</param>
/// <param name="StudiedQuestions">Total study-state rows = distinct studied questions.</param>
/// <param name="DueToday">Rows with <c>next_due_at &lt;= now</c>.</param>
/// <param name="Upcoming">Due-count per calendar day over the next 7 days (only days with a due count).</param>
/// <param name="TotalTimesSeen">Sum of <c>TimesSeen</c> across all rows (retention denominator).</param>
/// <param name="TotalTimesKnown">Sum of <c>TimesKnown</c> across all rows (retention numerator).</param>
public sealed record StudyStateAggregate(
    IReadOnlyList<StudyStatusCount> StatusCounts,
    int StudiedQuestions,
    int DueToday,
    IReadOnlyList<UpcomingDueCount> Upcoming,
    long TotalTimesSeen,
    long TotalTimesKnown);

/// <summary>Row count for a single study-status.</summary>
public sealed record StudyStatusCount(StudyStatus Status, int Count);

/// <summary>Due-question count for one upcoming calendar day.</summary>
public sealed record UpcomingDueCount(DateOnly Date, int Due);
