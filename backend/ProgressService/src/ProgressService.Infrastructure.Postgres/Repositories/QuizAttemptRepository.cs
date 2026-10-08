using System.Linq.Expressions;
using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Quizzes;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class QuizAttemptRepository : IQuizAttemptRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public QuizAttemptRepository(ProgressDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task AddAsync(QuizAttempt attempt, CancellationToken cancellationToken = default)
    {
        await _dbContext.QuizAttempts.AddAsync(attempt, cancellationToken);
    }

    public async Task<IReadOnlyList<QuizAttempt>> GetManyByAsync(
        Expression<Func<QuizAttempt, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.QuizAttempts
            .AsNoTracking()
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public async Task<(QuizAttempt? Best, QuizAttempt? Latest)> GetBestAndLatestAsync(
        Guid userId,
        Guid quizId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<QuizAttempt> attempts = _dbContext.QuizAttempts
            .AsNoTracking()
            .Where(attempt => attempt.UserId == userId && attempt.QuizId == quizId);

        QuizAttempt? best = await attempts
            .OrderByDescending(attempt => attempt.ScorePercent)
            .ThenByDescending(attempt => attempt.SubmittedAt)
            .ThenByDescending(attempt => attempt.Id)
            .FirstOrDefaultAsync(cancellationToken);

        QuizAttempt? latest = await attempts
            .OrderByDescending(attempt => attempt.SubmittedAt)
            .ThenByDescending(attempt => attempt.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return (best, latest);
    }

    public async Task<int> DeleteByQuizIdAsync(Guid quizId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM quiz_attempts WHERE quiz_id = @QuizId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { QuizId = quizId },
                cancellationToken: cancellationToken));
    }
}
