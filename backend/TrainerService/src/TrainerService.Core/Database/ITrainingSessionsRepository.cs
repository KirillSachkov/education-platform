using System.Linq.Expressions;
using TrainerService.Domain;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Core.Database;

public interface ITrainingSessionsRepository
{
    Task AddAsync(TrainingSession session, CancellationToken ct = default);

    Task<Result<TrainingSession, Error>> GetByAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>Eager-loads the <see cref="TrainingSession.Items"/> child collection.</summary>
    Task<Result<TrainingSession, Error>> GetWithItemsAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<TrainingSession>> GetManyByAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>Same as <see cref="GetManyByAsync"/> but eager-loads each session's items.</summary>
    Task<IReadOnlyList<TrainingSession>> GetManyWithItemsByAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    ///     Recent sessions for a user (newest-first, SQL-bounded by <paramref name="limit"/>),
    ///     eager-loading items. Optional mode + track filters are pushed to SQL so the user's whole
    ///     session history is never materialised in memory. Track filter is a plain equality on the
    ///     stored <see cref="TrainingSession.TrackId"/> — no array-overlap (Npgsql can't translate it) (#568).
    /// </summary>
    Task<IReadOnlyList<TrainingSession>> GetRecentForUserAsync(
        Guid userId,
        TrainingMode? mode,
        Guid? trackId,
        int limit,
        CancellationToken ct = default);

    /// <summary>Recent session headers without loading child items, for the progress summary.</summary>
    Task<IReadOnlyList<TrainingSession>> GetRecentSummariesForUserAsync(
        Guid userId,
        int limit,
        CancellationToken ct = default);

    /// <summary>SQL-bounded ids of PENDING/GRADING sessions for the periodic recovery sweep.</summary>
    Task<IReadOnlyList<Guid>> GetIdsAwaitingGradingAsync(
        int limit,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    ///     Sessions a user started on/after <paramref name="since"/> (UTC), eager-loading items, for the
    ///     activity timeline (<c>GET /trainer/stats/activity</c>, #568). Bounded by the date window, not
    ///     unbounded all-time history — grouping by <c>started_at::date</c> is done in memory by the caller.
    /// </summary>
    Task<IReadOnlyList<TrainingSession>> GetWithItemsStartedSinceAsync(
        Guid userId,
        DateTime since,
        CancellationToken ct = default);

    /// <summary>
    ///     Distinct calendar dates (UTC) on which the user started a session, newest-first. Lightweight
    ///     projection (no items) for all-time streak computation (<c>GET /trainer/stats/activity</c>, #568).
    /// </summary>
    Task<IReadOnlyList<DateOnly>> GetDistinctSessionDatesAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    ///     All-time answered/correct/per-difficulty aggregation over the user's session items, computed in
    ///     SQL (no item materialisation) for the stats summary (<c>GET /trainer/stats/summary</c>, #568).
    ///     Counts DISTINCT scored questions by their LATEST attempt (#691): re-answering one question N×
    ///     counts once, and «correct» follows the latest verdict. Difficulty is the snapshot
    ///     <see cref="TrainingSessions.TrainingSessionItem.Difficulty"/> of that latest attempt.
    /// </summary>
    Task<SessionAnswerAggregate> GetAnswerAggregateForUserAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    ///     The latest scored attempt per DISTINCT question the user answered in <paramref name="topicId"/>
    ///     (#691) — the source rows for the derived, difficulty-weighted topic mastery. Only graded
    ///     attempts (a non-null <c>score_percent</c> + <c>answered_at</c>) are considered; one row per
    ///     question (the most-recent <c>answered_at</c>).
    /// </summary>
    Task<IReadOnlyList<QuestionLatestScore>> GetLatestScoredAnswersPerQuestionAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default);

    /// <summary>
    ///     Topic ids for which the user has at least one scored MIDDLE or SENIOR answered question (#691) —
    ///     the gate that stops a topic being crowned «strong» off Junior-only answers
    ///     (<c>GET /trainer/stats/strengths</c>).
    /// </summary>
    Task<IReadOnlySet<Guid>> GetTopicIdsWithMidOrSeniorAnswersAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    ///     Every distinct (user, topic, question) latest scored attempt across ALL users — the source for
    ///     the <c>recompute-mastery</c> backfill CLI (#691), which re-derives every <c>topic_masteries</c>
    ///     row from history. Only graded attempts; one row per (user, topic, question).
    /// </summary>
    Task<IReadOnlyList<UserTopicQuestionScore>> GetAllLatestScoredAnswersAsync(
        CancellationToken ct = default);

    /// <summary>
    ///     Completed MOCK sessions of the user (ascending by completion date) with their AI feedback JSON,
    ///     for the mock trend (<c>GET /trainer/stats/mock-trend</c>, #568). Only sessions with a final score.
    /// </summary>
    Task<IReadOnlyList<TrainingSession>> GetCompletedMockSessionsAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    ///     Total number of training sessions the user has started (any mode/status), computed in SQL
    ///     (no item materialisation) — sample-size context for the strengths/weaknesses assessment (#614 H).
    /// </summary>
    Task<int> CountSessionsForUserAsync(
        Guid userId,
        CancellationToken ct = default);
}

/// <summary>
///     SQL-side aggregation of a user's answered session items (#568 stats). <paramref name="TotalAnswered"/> /
///     <paramref name="TotalCorrect"/> are all-time totals over auto-graded items; <paramref name="ByDifficulty"/>
///     keys are the snapshot difficulty literal (JUNIOR/MIDDLE/SENIOR, or null when unspecified).
/// </summary>
public sealed record SessionAnswerAggregate(
    int TotalAnswered,
    int TotalCorrect,
    IReadOnlyList<DifficultyAnswerCount> ByDifficulty);

/// <summary>Per-difficulty answered/correct counts over distinct-question latest attempts.</summary>
public sealed record DifficultyAnswerCount(
    string? Difficulty,
    int Answered,
    int Correct);

/// <summary>
///     One (user, topic, question) latest scored attempt for the global mastery backfill (#691):
///     <paramref name="ScorePercent"/> + snapshot <paramref name="Difficulty"/> drive the weighted
///     average; <paramref name="AnsweredAt"/> is the latest attempt's timestamp (newest per topic →
///     <c>last_practised_at</c>).
/// </summary>
public sealed record UserTopicQuestionScore(
    Guid UserId,
    Guid TopicId,
    Guid QuestionId,
    int ScorePercent,
    string? Difficulty,
    DateTime AnsweredAt);
