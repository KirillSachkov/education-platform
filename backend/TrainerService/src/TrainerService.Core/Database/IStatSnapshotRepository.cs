using TrainerService.Domain.Snapshots;

namespace TrainerService.Core.Database;

/// <summary>
/// Read/write access for the daily stat snapshots (#681 T1). The write side recomputes the three
/// snapshot families (platform KPIs, per-user-per-topic mastery, per-question accuracy) from the
/// source tables (<c>training_sessions</c> + <c>training_session_items</c> + <c>topic_masteries</c>
/// + <c>ai_usage</c> for cost) and (re)writes the rows for a date idempotently. The read side serves
/// the three downstream T6 views. The background job and the <c>snapshot-stats</c> CLI both call
/// <see cref="WriteSnapshotsAsync"/>.
/// </summary>
public interface IStatSnapshotRepository
{
    /// <summary>
    /// Computes today's aggregates from the source tables and (re)writes the snapshot rows for
    /// <paramref name="snapshotDate"/>. Idempotent per date — the date's existing rows in all three
    /// tables are replaced atomically, so re-running for the same day never duplicates.
    /// </summary>
    Task<StatSnapshotRunResult> WriteSnapshotsAsync(DateOnly snapshotDate, CancellationToken ct = default);

    /// <summary>Platform daily KPI series in <c>[from, to]</c> (inclusive), oldest-first — owner trend curve.</summary>
    Task<IReadOnlyList<DailyStatSnapshot>> GetDailySeriesAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// A user's topic-mastery snapshots on exactly two dates (today vs ~month ago) — student
    /// «vs месяц назад» comparison. Returns rows for whichever of the two dates exist.
    /// </summary>
    Task<IReadOnlyList<TopicMasterySnapshot>> GetUserTopicMasteryOnDatesAsync(
        Guid userId, DateOnly dateA, DateOnly dateB, CancellationToken ct = default);

    /// <summary>A question's accuracy snapshots in <c>[from, to]</c> (inclusive), oldest-first — quality over time.</summary>
    Task<IReadOnlyList<QuestionAccuracySnapshot>> GetQuestionAccuracySeriesAsync(
        Guid questionId, DateOnly from, DateOnly to, CancellationToken ct = default);
}

/// <summary>Row counts written by one snapshot run (one daily row + N mastery + M question rows).</summary>
public sealed record StatSnapshotRunResult(int DailyRows, int TopicMasteryRows, int QuestionAccuracyRows);
