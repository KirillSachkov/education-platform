using System.Linq.Expressions;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;

namespace AssignmentReviewService.Core.Database;

public interface IAiReviewsRepository
{
    Task AddAsync(AiReview review, CancellationToken ct = default);

    /// <summary>
    ///     Атомарный gate против concurrent run-iteration. Делает
    ///     <c>UPDATE ai_reviews SET status='RUNNING' WHERE id=@id AND status&lt;&gt;'RUNNING'</c>.
    ///     Возвращает true если этот caller — exclusive owner новой iteration.
    ///     false → другой запрос уже забрал lock; вернуть <c>review.already_running</c>.
    /// </summary>
    Task<DateTimeOffset?> TryAcquireRunningLockAsync(Guid reviewId, CancellationToken ct = default);

    Task<bool> IsRunningLeaseCurrentAsync(
        Guid reviewId,
        DateTimeOffset acquiredAt,
        CancellationToken ct = default);

    /// <summary>
    ///     Liveness-heartbeat (#690): bump'ит <c>heartbeat_at = now()</c> для RUNNING-ревью.
    ///     Вызывается после каждого batch'а длинного multi-batch ревью, чтобы
    ///     stale-watchdog не считал прогрессирующее ревью зависшим (см.
    ///     <c>StaleReviewMaxAgeMinutes</c>). НЕ трогает <c>updated_at</c> — running-lease
    ///     (fencing на <c>updated_at</c>) остаётся валидным. No-op если ревью уже не RUNNING
    ///     (например, watchdog/restart уже переотправил его — тогда текущий прогон
    ///     superseded и завершится сам).
    /// </summary>
    /// <remarks>
    ///     Должен вызываться ВНЕ открытой DB-транзакции: watchdog читает heartbeat_at с
    ///     отдельного connection'а, и незакоммиченная запись ему не видна → защита от
    ///     starvation не сработает. Сейчас вызывается во время LLM-фазы RunIteration, где
    ///     транзакция ещё не открыта (outbox-транзакция стартует только на финальном
    ///     SaveChangesAsync) — raw UPDATE авто-коммитится.
    /// </remarks>
    Task HeartbeatRunningAsync(Guid reviewId, CancellationToken ct = default);

    Task<IReadOnlyList<AiReview>> ListActiveAsync(CancellationToken ct = default);

    /// <summary>
    ///     Eager-load <see cref="AiReview.Iterations"/> через явный <c>Include</c>.
    ///     См. docs/agents/backend-transactions.md правило #3.
    /// </summary>
    Task<AiReview?> GetByAsync(
        Expression<Func<AiReview, bool>> predicate, CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<AiReview, bool>> predicate, CancellationToken ct = default);

    /// <summary>
    ///     Считает iteration'ы юзера за окно [from, to). Используется
    ///     <c>RateLimitChecker</c> для daily-cap'ов.
    /// </summary>
    Task<int> CountIterationsForUserSinceAsync(
        Guid userId,
        DateTimeOffset since,
        CancellationToken ct = default);

    /// <summary>
    ///     Считает iteration'ы для submission'а. Per-submission cap (anti-flood
    ///     если студент тыкает кнопку 1000 раз).
    /// </summary>
    Task<int> CountIterationsForSubmissionAsync(
        Guid submissionId,
        CancellationToken ct = default);

    /// <summary>
    ///     Считает iteration'ы по AiReview.AuthorId с начала окна <paramref name="since"/>.
    ///     Issue #327 — daily cost guard на уровне автора (выше user-level cap'а).
    /// </summary>
    Task<int> CountIterationsForAuthorSinceAsync(
        Guid authorId,
        DateTimeOffset since,
        CancellationToken ct = default);

    /// <summary>
    ///     Возвращает iteration по id или null. Issue #327 — `SubmitFeedback`
    ///     handler'у нужен verdict (metric label) до записи фидбэка.
    /// </summary>
    Task<Domain.Reviews.AiReviewIteration?> GetIterationByIdAsync(
        Guid iterationId,
        CancellationToken ct = default);

    /// <summary>
    ///     Последняя COMPLETED-итерация (с непустым commit_sha) того же студента по
    ///     тому же PR из ДРУГИХ review'ов (#334). На ре-сабмите создаётся новый AiReview
    ///     под новую submission, поэтому incremental baseline нужно искать поперёк
    ///     review'ов — иначе перепроверка ревьюит весь PR заново вместо новых коммитов.
    /// </summary>
    Task<AiReviewIteration?> GetLatestCompletedIterationForPullRequestAsync(
        VcsProvider provider,
        string repoFullName,
        int pullNumber,
        Guid userId,
        Guid excludeReviewId,
        CancellationToken ct = default);

    /// <summary>
    ///     Cascade-delete всех <c>ai_reviews</c> для удалённого issue. Iteration'ы
    ///     удаляются через FK ON DELETE CASCADE. Используется
    ///     <c>IssueHardDeletedAssignmentReviewHandler</c>.
    /// </summary>
    Task<int> DeleteByIssueIdAsync(Guid issueId, CancellationToken ct = default);

    /// <summary>
    ///     Последний <see cref="AiReview"/> под указанный PR (<paramref name="repoFullName"/> +
    ///     <paramref name="pullNumber"/>). Match по repo — case-insensitive (GitHub owner/repo
    ///     регистронезависимы; webhook несёт каноничный casing, submission-URL — тот, что набрал
    ///     студент). На ре-сабмите под тот же PR создаётся новый AiReview, поэтому берём самый
    ///     свежий по <c>created_at</c>. Используется webhook-ingest'ом комментариев студента (#713).
    ///     null → PR не отслеживается (нет AiReview) → webhook no-op.
    /// </summary>
    Task<AiReview?> GetLatestByPullRequestAsync(
        VcsProvider provider,
        string repoFullName,
        int pullNumber,
        CancellationToken ct = default);
}
