using System.Data;
using System.Data.Common;
using Dapper;
using Microsoft.EntityFrameworkCore.Storage;
using TrainerService.Core.Database;
using TrainerService.Domain.Snapshots;

namespace TrainerService.Infrastructure.Postgres.Repositories;

/// <summary>
///     Daily stat snapshots (#681 T1). Compute = Dapper raw-SQL aggregates over the source tables
///     (no entity materialization, mirrors <c>GetAdminStats</c>); write = EF replace-then-insert in a
///     transaction (idempotent per date); read = EF over the snapshot tables (entities with private
///     ctors). Timestamps are stored UTC, so day windows use a half-open <c>[dayStart, dayEnd)</c>
///     range over the indexed timestamp columns rather than a per-row date cast.
/// </summary>
internal sealed class StatSnapshotRepository : IStatSnapshotRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public StatSnapshotRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    // --- compute SQL (source tables) ---

    // Platform KPIs for the day. Counts are narrowed to int and the cost sum is kept as bigint so Dapper
    // maps each column onto the matching field type. Open-grade count and average accuracy come from graded
    // session items; total cost from the ai usage ledger; session counts from training sessions.
    private const string DailyAggSql = """
        SELECT
            (SELECT COUNT(*)::int FROM training_sessions
                WHERE started_at >= @DayStart AND started_at < @DayEnd)                       AS SessionsStarted,
            (SELECT COUNT(DISTINCT user_id)::int FROM training_sessions
                WHERE started_at >= @DayStart AND started_at < @DayEnd)                       AS ActiveUsers,
            (SELECT COUNT(*)::int FROM training_sessions
                WHERE completed_at IS NOT NULL AND completed_at >= @DayStart AND completed_at < @DayEnd) AS CompletedSessions,
            (SELECT COUNT(*)::int FROM training_session_items
                WHERE question_type = 'OPEN_TEXT' AND score_percent IS NOT NULL
                  AND answered_at >= @DayStart AND answered_at < @DayEnd)                      AS OpenGrades,
            (SELECT COALESCE(SUM(cost_micro_rub), 0)::bigint FROM ai_usage
                WHERE created_at >= @DayStart AND created_at < @DayEnd)                        AS TotalCostMicroRub,
            (SELECT COALESCE(AVG(score_percent), 0)::double precision FROM training_session_items
                WHERE score_percent IS NOT NULL
                  AND answered_at >= @DayStart AND answered_at < @DayEnd)                      AS AvgAccuracyPct;
        """;

    // Point-in-time mastery: snapshot every current topic mastery row (independent of the day window).
    private const string MasterySql = """
        SELECT user_id AS UserId, topic_id AS TopicId, mastery_percent::double precision AS Mastery
        FROM topic_masteries;
        """;

    // Per-question accuracy over the day's graded answers (verdict final, not pending). The grouping only
    // includes matching rows, so the attempt count is at least one and the accuracy never divides by zero.
    private const string QuestionAccuracySql = """
        SELECT
            question_id                                                            AS QuestionId,
            COUNT(*)::int                                                          AS Attempts,
            (COUNT(*) FILTER (WHERE verdict = 'CORRECT'))::int                     AS Correct,
            (COUNT(*) FILTER (WHERE verdict = 'CORRECT') * 100.0 / COUNT(*))::double precision AS AccuracyPct
        FROM training_session_items
        WHERE answered_at >= @DayStart AND answered_at < @DayEnd
          AND verdict IS NOT NULL AND verdict <> 'PENDING'
        GROUP BY question_id;
        """;

    public async Task<StatSnapshotRunResult> WriteSnapshotsAsync(
        DateOnly snapshotDate, CancellationToken ct = default)
    {
        DateTimeOffset dayStart = new(snapshotDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset dayEnd = dayStart.AddDays(1);
        object dayArgs = new { DayStart = dayStart, DayEnd = dayEnd };

        DbConnection connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        // --- compute (fully materialized; no open reader before the EF writes) ---
        DailyAggRow daily = await connection.QuerySingleAsync<DailyAggRow>(
            new CommandDefinition(DailyAggSql, dayArgs, cancellationToken: ct));

        IReadOnlyList<MasteryRow> masteryRows =
            (await connection.QueryAsync<MasteryRow>(
                new CommandDefinition(MasterySql, cancellationToken: ct))).ToList();

        IReadOnlyList<QuestionAccuracyRow> questionRows =
            (await connection.QueryAsync<QuestionAccuracyRow>(
                new CommandDefinition(QuestionAccuracySql, dayArgs, cancellationToken: ct))).ToList();

        // --- write (replace the date's rows atomically → idempotent per day) ---
        await using IDbContextTransaction tx = await _dbContext.Database.BeginTransactionAsync(ct);

        await _dbContext.DailyStatSnapshots
            .Where(s => s.SnapshotDate == snapshotDate).ExecuteDeleteAsync(ct);
        await _dbContext.TopicMasterySnapshots
            .Where(s => s.SnapshotDate == snapshotDate).ExecuteDeleteAsync(ct);
        await _dbContext.QuestionAccuracySnapshots
            .Where(s => s.SnapshotDate == snapshotDate).ExecuteDeleteAsync(ct);

        _dbContext.DailyStatSnapshots.Add(DailyStatSnapshot.Create(
            snapshotDate,
            daily.SessionsStarted,
            daily.ActiveUsers,
            daily.CompletedSessions,
            daily.OpenGrades,
            daily.TotalCostMicroRub,
            daily.AvgAccuracyPct));

        foreach (MasteryRow m in masteryRows)
            _dbContext.TopicMasterySnapshots.Add(
                TopicMasterySnapshot.Create(snapshotDate, m.UserId, m.TopicId, m.Mastery));

        foreach (QuestionAccuracyRow q in questionRows)
            _dbContext.QuestionAccuracySnapshots.Add(
                QuestionAccuracySnapshot.Create(snapshotDate, q.QuestionId, q.Attempts, q.Correct, q.AccuracyPct));

        await _dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new StatSnapshotRunResult(1, masteryRows.Count, questionRows.Count);
    }

    public async Task<IReadOnlyList<DailyStatSnapshot>> GetDailySeriesAsync(
        DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await _dbContext.DailyStatSnapshots
            .AsNoTracking()
            .Where(s => s.SnapshotDate >= from && s.SnapshotDate <= to)
            .OrderBy(s => s.SnapshotDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TopicMasterySnapshot>> GetUserTopicMasteryOnDatesAsync(
        Guid userId, DateOnly dateA, DateOnly dateB, CancellationToken ct = default) =>
        await _dbContext.TopicMasterySnapshots
            .AsNoTracking()
            .Where(s => s.UserId == userId && (s.SnapshotDate == dateA || s.SnapshotDate == dateB))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<QuestionAccuracySnapshot>> GetQuestionAccuracySeriesAsync(
        Guid questionId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await _dbContext.QuestionAccuracySnapshots
            .AsNoTracking()
            .Where(s => s.QuestionId == questionId && s.SnapshotDate >= from && s.SnapshotDate <= to)
            .OrderBy(s => s.SnapshotDate)
            .ToListAsync(ct);

    private sealed record DailyAggRow
    {
        public int SessionsStarted { get; init; }
        public int ActiveUsers { get; init; }
        public int CompletedSessions { get; init; }
        public int OpenGrades { get; init; }
        public long TotalCostMicroRub { get; init; }
        public double AvgAccuracyPct { get; init; }
    }

    private sealed record MasteryRow(Guid UserId, Guid TopicId, double Mastery);

    private sealed record QuestionAccuracyRow(Guid QuestionId, int Attempts, int Correct, double AccuracyPct);
}
