using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.QuestionStudyStates;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class QuestionStudyStatesRepository : IQuestionStudyStatesRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public QuestionStudyStatesRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(QuestionStudyState state, CancellationToken ct = default) =>
        await _dbContext.QuestionStudyStates.AddAsync(state, ct);

    public async Task<Result<QuestionStudyState, Error>> GetByAsync(
        Guid userId,
        Guid questionId,
        CancellationToken ct = default)
    {
        QuestionStudyState? state = await _dbContext.QuestionStudyStates
            .FirstOrDefaultAsync(s => s.UserId == userId && s.QuestionId == questionId, ct);

        return state is null
            ? TrainerServiceErrors.StudyState.NotFound(userId, questionId)
            : state;
    }

    public async Task<IReadOnlyList<QuestionStudyState>> GetManyForUserAsync(
        Guid userId,
        Guid? topicId,
        StudyStatus? status,
        bool dueOnly,
        int limit,
        CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        IQueryable<QuestionStudyState> query = _dbContext.QuestionStudyStates
            .Where(s => s.UserId == userId);

        // Plain equality filters — pushed to SQL, no array-overlap (Npgsql can't translate it, #568).
        if (topicId is { } tid)
            query = query.Where(s => s.TopicId == tid);

        if (status is { } st)
            query = query.Where(s => s.Status == st);

        if (dueOnly)
            query = query.Where(s => s.NextDueAt != null && s.NextDueAt <= now);

        // Most-due first (next_due_at ASC, NULLS LAST in PG), then recently seen.
        return await query
            .OrderBy(s => s.NextDueAt)
            .ThenByDescending(s => s.LastSeenAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<QuestionStudyState>> GetForQuestionsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> questionIds,
        CancellationToken ct = default)
    {
        if (questionIds.Count == 0)
            return [];

        // parameter.Contains → SQL `question_id = ANY(@ids)` (column-on-set equality, fine; the #568
        // gotcha is array-COLUMN overlap, which this is not).
        return await _dbContext.QuestionStudyStates
            .Where(s => s.UserId == userId && questionIds.Contains(s.QuestionId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<QuestionStudyState>> GetAllForUserAsync(
        Guid userId,
        CancellationToken ct = default) =>
        await _dbContext.QuestionStudyStates
            .Where(s => s.UserId == userId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<QuestionStudyState>> GetMistakesForUserAsync(
        Guid userId,
        Guid? topicId,
        int limit,
        CancellationToken ct = default)
    {
        IQueryable<QuestionStudyState> query = _dbContext.QuestionStudyStates
            .Where(s => s.UserId == userId
                && (s.Status == StudyStatus.WRONG || s.Status == StudyStatus.REVIEW));

        if (topicId is { } tid)
            query = query.Where(s => s.TopicId == tid);

        return await query
            .OrderByDescending(s => s.LastSeenAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<StudyStateAggregate> GetStudyStateAggregateForUserAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset upcomingHorizon = now.AddDays(7);

        IQueryable<QuestionStudyState> userStates = _dbContext.QuestionStudyStates
            .Where(s => s.UserId == userId);

        // Per-status counts + total rows + retention sums in one pass.
        var statusRows = await userStates
            .GroupBy(s => s.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                TimesSeen = (long)g.Sum(s => s.TimesSeen),
                TimesKnown = (long)g.Sum(s => s.TimesKnown),
            })
            .ToListAsync(ct);

        var statusCounts = statusRows
            .Select(r => new StudyStatusCount(r.Status, r.Count))
            .ToList();

        int studied = statusRows.Sum(r => r.Count);
        long totalSeen = statusRows.Sum(r => r.TimesSeen);
        long totalKnown = statusRows.Sum(r => r.TimesKnown);

        int dueToday = await userStates
            .CountAsync(s => s.NextDueAt != null && s.NextDueAt <= now, ct);

        // Upcoming 7-day forecast: rows due strictly after now and within the horizon, grouped by due date.
        var upcomingRows = await userStates
            .Where(s => s.NextDueAt != null && s.NextDueAt > now && s.NextDueAt <= upcomingHorizon)
            .GroupBy(s => DateOnly.FromDateTime(s.NextDueAt!.Value.UtcDateTime))
            .Select(g => new { Date = g.Key, Due = g.Count() })
            .ToListAsync(ct);

        var upcoming = upcomingRows
            .OrderBy(r => r.Date)
            .Select(r => new UpcomingDueCount(r.Date, r.Due))
            .ToList();

        return new StudyStateAggregate(statusCounts, studied, dueToday, upcoming, totalSeen, totalKnown);
    }
}
