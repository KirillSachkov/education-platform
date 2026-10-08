using System.Linq.Expressions;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Gamification;

namespace ProgressService.Infrastructure.Postgres.Repositories;

/// <summary>
/// Репозиторий ledger-записей начисления XP.
/// </summary>
public sealed class XpAwardRepository : IXpAwardRepository
{
    private readonly ProgressDbContext _dbContext;

    public XpAwardRepository(ProgressDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// Добавляет ledger-запись начисления XP в текущий контекст.
    /// </summary>
    public async Task AddAsync(XpAward xpAward, CancellationToken cancellationToken = default)
    {
        await _dbContext.XpAwards.AddAsync(xpAward, cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<XpAward, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        // Sначала проверяем Local (tracked сущности) — внутри одной транзакции мог быть AddAsync,
        // но SaveChanges ещё не вызывался, и БД не видит запись.
        Func<XpAward, bool> compiled = predicate.Compile();
        if (_dbContext.XpAwards.Local.Any(compiled))
        {
            return true;
        }

        return await _dbContext.XpAwards.AnyAsync(predicate, cancellationToken);
    }

    public async Task<XpAward?> FindByAsync(
        Expression<Func<XpAward, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Func<XpAward, bool> compiled = predicate.Compile();
        XpAward? local = _dbContext.XpAwards.Local.FirstOrDefault(compiled);
        if (local is not null)
        {
            return local;
        }

        return await _dbContext.XpAwards.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public void Remove(XpAward xpAward) => _dbContext.XpAwards.Remove(xpAward);
}
