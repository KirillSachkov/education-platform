using System.Linq.Expressions;
using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using Core.Database;
using Dapper;

namespace AssignmentReviewService.Infrastructure.Postgres.Repositories;

internal sealed class AiReviewsRepository : IAiReviewsRepository
{
    private readonly AssignmentReviewServiceDbContext _db;
    private readonly ITransactionManager _transactions;

    public AiReviewsRepository(
        AssignmentReviewServiceDbContext db,
        ITransactionManager transactions)
    {
        _db = db;
        _transactions = transactions;
    }

    public async Task AddAsync(AiReview review, CancellationToken ct = default) =>
        await _db.AiReviews.AddAsync(review, ct);

    public async Task<DateTimeOffset?> TryAcquireRunningLockAsync(Guid reviewId, CancellationToken ct = default)
    {
        // Conditional UPDATE — outside EF tracking, чтобы race условие фиксилось
        // на DB-уровне (Postgres гарантирует exactly-один UPDATE по строке).
        // EF tracker увидит status=RUNNING на следующем reload через GetByAsync.
        DateTimeOffset acquiredAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        const string sql = """
            UPDATE assignment_review.ai_reviews
            SET status = 'RUNNING', updated_at = @AcquiredAt
            WHERE id = @ReviewId
              AND status <> 'RUNNING'
            RETURNING id
            """;

        System.Data.Common.DbConnection conn = _transactions.GetDbConnection();
        Guid? acquired = await conn.QueryFirstOrDefaultAsync<Guid?>(new CommandDefinition(
            sql,
            new { ReviewId = reviewId, AcquiredAt = acquiredAt },
            cancellationToken: ct));

        return acquired.HasValue ? acquiredAt : null;
    }

    public async Task HeartbeatRunningAsync(Guid reviewId, CancellationToken ct = default)
    {
        // Raw SQL — намеренно НЕ трогаем updated_at (lease fencing token), только
        // heartbeat_at. Guard `status='RUNNING'` делает heartbeat no-op'ом, если
        // ревью уже переотправлено watchdog'ом/рестартом — текущий прогон в этом
        // случае всё равно superseded и завершится сам (lease-check в RunIteration).
        const string sql = """
            UPDATE assignment_review.ai_reviews
            SET heartbeat_at = now()
            WHERE id = @ReviewId
              AND status = 'RUNNING'
            """;

        System.Data.Common.DbConnection conn = _transactions.GetDbConnection();
        await conn.ExecuteAsync(new CommandDefinition(
            sql,
            new { ReviewId = reviewId },
            cancellationToken: ct));
    }

    public async Task<bool> IsRunningLeaseCurrentAsync(
        Guid reviewId,
        DateTimeOffset acquiredAt,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM assignment_review.ai_reviews
                WHERE id = @ReviewId
                  AND status = 'RUNNING'
                  AND updated_at = @AcquiredAt
            )
            """;

        System.Data.Common.DbConnection conn = _transactions.GetDbConnection();
        return await conn.QuerySingleAsync<bool>(new CommandDefinition(
            sql,
            new { ReviewId = reviewId, AcquiredAt = acquiredAt },
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AiReview>> ListActiveAsync(CancellationToken ct = default) =>
        await _db.AiReviews
            .Include(r => r.Iterations)
            .Where(r => r.Status == AiReviewStatus.QUEUED || r.Status == AiReviewStatus.RUNNING)
            .ToListAsync(ct);

    public Task<AiReview?> GetByAsync(
        Expression<Func<AiReview, bool>> predicate, CancellationToken ct = default) =>
        _db.AiReviews
            .Include(r => r.Iterations)
            .FirstOrDefaultAsync(predicate, ct);

    public Task<bool> ExistsAsync(
        Expression<Func<AiReview, bool>> predicate, CancellationToken ct = default) =>
        _db.AiReviews.AnyAsync(predicate, ct);

    public Task<int> CountIterationsForUserSinceAsync(
        Guid userId, DateTimeOffset since, CancellationToken ct = default) =>
        _db.AiReviewIterations
            .Where(it => it.StartedAt >= since)
            .Where(it => _db.AiReviews
                .Any(r => r.Id == it.AiReviewId && r.UserId == userId))
            .CountAsync(ct);

    public Task<int> CountIterationsForSubmissionAsync(
        Guid submissionId, CancellationToken ct = default) =>
        _db.AiReviewIterations
            .Where(it => _db.AiReviews
                .Any(r => r.Id == it.AiReviewId && r.SubmissionId == submissionId))
            .CountAsync(ct);

    public Task<int> CountIterationsForAuthorSinceAsync(
        Guid authorId, DateTimeOffset since, CancellationToken ct = default) =>
        _db.AiReviewIterations
            .Where(it => it.StartedAt >= since)
            .Where(it => _db.AiReviews
                .Any(r => r.Id == it.AiReviewId && r.AuthorId == authorId))
            .CountAsync(ct);

    public Task<AiReviewIteration?> GetIterationByIdAsync(
        Guid iterationId, CancellationToken ct = default) =>
        _db.AiReviewIterations.FirstOrDefaultAsync(i => i.Id == iterationId, ct);

    public Task<AiReviewIteration?> GetLatestCompletedIterationForPullRequestAsync(
        VcsProvider provider,
        string repoFullName,
        int pullNumber,
        Guid userId,
        Guid excludeReviewId,
        CancellationToken ct = default) =>
        _db.AiReviewIterations
            .Where(it => it.Status == AiReviewIterationStatus.COMPLETED && it.CommitSha != "")
            .Where(it => _db.AiReviews.Any(r =>
                r.Id == it.AiReviewId
                && r.Id != excludeReviewId
                && r.Provider == provider
                && r.RepoFullName == repoFullName
                && r.PullNumber == pullNumber
                && r.UserId == userId))
            .OrderByDescending(it => it.CompletedAt)
            .FirstOrDefaultAsync(ct);

    public Task<int> DeleteByIssueIdAsync(Guid issueId, CancellationToken ct = default) =>
        _db.AiReviews.Where(r => r.IssueId == issueId).ExecuteDeleteAsync(ct);

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization", "CA1304:Specify CultureInfo",
        Justification = "LINQ-to-SQL: .ToLower() транслируется Npgsql в SQL lower(), не in-memory string op.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization", "CA1311:Specify a culture or use an invariant version",
        Justification = "LINQ-to-SQL: .ToLower() транслируется Npgsql в SQL lower(), не in-memory string op.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization", "CA1862:Prefer StringComparison overload",
        Justification = "StringComparison.OrdinalIgnoreCase не транслируется Npgsql; нужен SQL lower().")]
    public Task<AiReview?> GetLatestByPullRequestAsync(
        VcsProvider provider,
        string repoFullName,
        int pullNumber,
        CancellationToken ct = default)
    {
        // Case-insensitive match: repo-имена регистронезависимы. .ToLower() → SQL lower()
        // (ILIKE опасен — '_' в repo-именах = wildcard). Repo-имена ASCII → lower() детерминирован.
        string repoFullNameLower = repoFullName.ToLowerInvariant();
        return _db.AiReviews
            .Where(r => r.Provider == provider
                        && r.PullNumber == pullNumber
                        && r.RepoFullName.ToLower() == repoFullNameLower)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % 10, value.Offset);
}
