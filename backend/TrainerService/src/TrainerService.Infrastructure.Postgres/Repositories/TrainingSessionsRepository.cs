using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using Dapper;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicMasteries;
using TrainerService.Domain.TrainingSessions;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class TrainingSessionsRepository : ITrainingSessionsRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public TrainingSessionsRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(TrainingSession session, CancellationToken ct = default) =>
        await _dbContext.TrainingSessions.AddAsync(session, ct);

    public async Task<Result<TrainingSession, Error>> GetByAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default)
    {
        TrainingSession? session = await _dbContext.TrainingSessions.FirstOrDefaultAsync(predicate, ct);
        return session is null
            ? TrainerServiceErrors.Session.NotFound(Guid.Empty)
            : session;
    }

    public async Task<Result<TrainingSession, Error>> GetWithItemsAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default)
    {
        TrainingSession? session = await _dbContext.TrainingSessions
            .Include(s => s.Items)
            .FirstOrDefaultAsync(predicate, ct);
        return session is null
            ? TrainerServiceErrors.Session.NotFound(Guid.Empty)
            : session;
    }

    public async Task<IReadOnlyList<TrainingSession>> GetManyByAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.TrainingSessions.Where(predicate).ToListAsync(ct);

    public async Task<IReadOnlyList<TrainingSession>> GetManyWithItemsByAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.TrainingSessions
            .Include(s => s.Items)
            .Where(predicate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TrainingSession>> GetRecentForUserAsync(
        Guid userId,
        TrainingMode? mode,
        Guid? trackId,
        int limit,
        CancellationToken ct = default)
    {
        IQueryable<TrainingSession> query = _dbContext.TrainingSessions
            .Where(s => s.UserId == userId);

        if (mode is { } modeFilter)
            query = query.Where(s => s.Mode == modeFilter);

        // Plain equality on the stored TrackId — SQL-indexable, no array-overlap on TopicIds
        // (Npgsql/EFCore 10 doesn't translate column.Any(Contains) / parameter.Any(column.Contains)) (#568).
        if (trackId is { } track)
            query = query.Where(s => s.TrackId == track);

        return await query
            .OrderByDescending(s => s.StartedAt)
            .Take(limit)
            .Include(s => s.Items)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TrainingSession>> GetRecentSummariesForUserAsync(
        Guid userId,
        int limit,
        CancellationToken ct = default) =>
        await _dbContext.TrainingSessions
            .Where(session => session.UserId == userId)
            .OrderByDescending(session => session.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetIdsAwaitingGradingAsync(
        int limit,
        CancellationToken ct = default) =>
        await _dbContext.TrainingSessions
            .AsNoTracking()
            .Where(s => s.GradingStatus == GradingStatus.PENDING || s.GradingStatus == GradingStatus.GRADING)
            .OrderBy(s => s.CompletedAt)
            .Select(s => s.Id)
            .Take(limit)
            .ToListAsync(ct);

    public Task<bool> ExistsAsync(
        Expression<Func<TrainingSession, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.TrainingSessions.AnyAsync(predicate, ct);

    public async Task<IReadOnlyList<TrainingSession>> GetWithItemsStartedSinceAsync(
        Guid userId,
        DateTime since,
        CancellationToken ct = default) =>
        await _dbContext.TrainingSessions
            .Where(s => s.UserId == userId && s.StartedAt >= since)
            .Include(s => s.Items)
            .OrderBy(s => s.StartedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<DateOnly>> GetDistinctSessionDatesAsync(
        Guid userId,
        CancellationToken ct = default) =>
        await _dbContext.TrainingSessions
            .Where(s => s.UserId == userId)
            .Select(s => DateOnly.FromDateTime(s.StartedAt))
            .Distinct()
            .OrderByDescending(d => d)
            .ToListAsync(ct);

    // Per-difficulty counts over DISTINCT scored questions by their LATEST attempt, issue 691. The
    // DISTINCT ON picks the most-recent answered row per question, then groups those by difficulty, so
    // re-answering one question many times counts once and "correct" follows the latest verdict.
    private const string DifficultyAggSql = """
        WITH latest AS (
            SELECT DISTINCT ON (i.question_id) i.difficulty AS difficulty, i.verdict AS verdict
            FROM training_session_items i
            JOIN training_sessions s ON s.id = i.session_id
            WHERE s.user_id = @UserId
              AND i.score_percent IS NOT NULL
              AND i.answered_at IS NOT NULL
            ORDER BY i.question_id, i.answered_at DESC
        )
        SELECT difficulty AS Difficulty,
               COUNT(*)::int AS Answered,
               (COUNT(*) FILTER (WHERE verdict = 'CORRECT'))::int AS Correct
        FROM latest
        GROUP BY difficulty;
        """;

    // Latest scored attempt per DISTINCT question in one topic — source rows for the derived mastery (#691).
    private const string LatestScoredPerQuestionSql = """
        SELECT DISTINCT ON (i.question_id)
            i.question_id  AS QuestionId,
            i.score_percent AS ScorePercent,
            i.difficulty   AS Difficulty
        FROM training_session_items i
        JOIN training_sessions s ON s.id = i.session_id
        WHERE s.user_id = @UserId
          AND i.topic_id = @TopicId
          AND i.score_percent IS NOT NULL
          AND i.answered_at IS NOT NULL
        ORDER BY i.question_id, i.answered_at DESC;
        """;

    // Latest scored attempt per (user, topic, DISTINCT question) across all users — backfill source (#691).
    private const string AllLatestScoredSql = """
        SELECT DISTINCT ON (s.user_id, i.topic_id, i.question_id)
            s.user_id      AS UserId,
            i.topic_id     AS TopicId,
            i.question_id  AS QuestionId,
            i.score_percent AS ScorePercent,
            i.difficulty   AS Difficulty,
            i.answered_at  AS AnsweredAt
        FROM training_session_items i
        JOIN training_sessions s ON s.id = i.session_id
        WHERE i.score_percent IS NOT NULL
          AND i.answered_at IS NOT NULL
        ORDER BY s.user_id, i.topic_id, i.question_id, i.answered_at DESC;
        """;

    public async Task<SessionAnswerAggregate> GetAnswerAggregateForUserAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        DbConnection connection = await OpenConnectionAsync(ct);

        IReadOnlyList<DifficultyAnswerCount> byDifficulty =
            (await connection.QueryAsync<DifficultyAnswerCount>(
                new CommandDefinition(DifficultyAggSql, new { UserId = userId }, cancellationToken: ct))).ToList();

        return new SessionAnswerAggregate(
            byDifficulty.Sum(r => r.Answered),
            byDifficulty.Sum(r => r.Correct),
            byDifficulty);
    }

    public async Task<IReadOnlyList<QuestionLatestScore>> GetLatestScoredAnswersPerQuestionAsync(
        Guid userId,
        Guid topicId,
        CancellationToken ct = default)
    {
        DbConnection connection = await OpenConnectionAsync(ct);

        return (await connection.QueryAsync<QuestionLatestScore>(
            new CommandDefinition(
                LatestScoredPerQuestionSql, new { UserId = userId, TopicId = topicId }, cancellationToken: ct)))
            .ToList();
    }

    public async Task<IReadOnlySet<Guid>> GetTopicIdsWithMidOrSeniorAnswersAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        List<Guid> topicIds = await _dbContext.TrainingSessions
            .Where(s => s.UserId == userId)
            .SelectMany(s => s.Items)
            .Where(i => i.ScorePercent != null
                && (i.Difficulty == "MIDDLE" || i.Difficulty == "SENIOR"))
            .Select(i => i.TopicId)
            .Distinct()
            .ToListAsync(ct);

        return topicIds.ToHashSet();
    }

    public async Task<IReadOnlyList<UserTopicQuestionScore>> GetAllLatestScoredAnswersAsync(
        CancellationToken ct = default)
    {
        DbConnection connection = await OpenConnectionAsync(ct);

        return (await connection.QueryAsync<UserTopicQuestionScore>(
            new CommandDefinition(AllLatestScoredSql, cancellationToken: ct))).ToList();
    }

    private async Task<DbConnection> OpenConnectionAsync(CancellationToken ct)
    {
        DbConnection connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        return connection;
    }

    public async Task<IReadOnlyList<TrainingSession>> GetCompletedMockSessionsAsync(
        Guid userId,
        CancellationToken ct = default) =>
        await _dbContext.TrainingSessions
            .Where(s => s.UserId == userId
                && s.Mode == TrainingMode.MOCK
                && s.Status == SessionStatus.COMPLETED
                && s.ScorePercent != null
                && s.CompletedAt != null)
            .OrderBy(s => s.CompletedAt)
            .ToListAsync(ct);

    public Task<int> CountSessionsForUserAsync(Guid userId, CancellationToken ct = default) =>
        _dbContext.TrainingSessions.CountAsync(s => s.UserId == userId, ct);
}
