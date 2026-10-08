using System.Linq.Expressions;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.LevelTests;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class LevelTestAttemptRepository : ILevelTestAttemptRepository
{
    private readonly ProgressDbContext _dbContext;

    public LevelTestAttemptRepository(ProgressDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(LevelTestAttempt attempt, CancellationToken cancellationToken = default)
    {
        await _dbContext.LevelTestAttempts.AddAsync(attempt, cancellationToken);
    }

    public async Task<LevelTestAttempt?> GetByAsync(
        Expression<Func<LevelTestAttempt, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.LevelTestAttempts
            .FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public async Task<IReadOnlyList<LevelTestAttempt>> GetManyByAsync(
        Expression<Func<LevelTestAttempt, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.LevelTestAttempts
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public async Task<LevelTestAttempt?> GetLatestByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.LevelTestAttempts
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
