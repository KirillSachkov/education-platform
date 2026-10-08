using System.Linq.Expressions;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Gamification;

namespace ProgressService.Infrastructure.Postgres.Repositories;

/// <summary>
/// Репозиторий агрегированной статистики пользователя по XP и уровню.
/// </summary>
public sealed class UserStatsRepository : IUserStatsRepository
{
    private readonly ProgressDbContext _dbContext;

    public UserStatsRepository(ProgressDbContext dbContext) => _dbContext = dbContext;

    /// <summary>
    /// Добавляет новую запись статистики пользователя в текущий контекст.
    /// </summary>
    public async Task AddAsync(UserGamificationStats stats, CancellationToken cancellationToken = default)
    {
        await _dbContext.UserGamificationStats.AddAsync(stats, cancellationToken);
    }

    /// <summary>
    /// Ищет статистику по пользователю сначала среди уже отслеживаемых сущностей контекста,
    /// затем в базе данных.
    /// </summary>
    public async Task<Result<UserGamificationStats, Error>> GetByAsync(
        Expression<Func<UserGamificationStats, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        UserGamificationStats? localStats = _dbContext.UserGamificationStats.Local
            .AsQueryable()
            .FirstOrDefault(predicate);
        if (localStats is not null)
        {
            return localStats;
        }

        UserGamificationStats? stats = await _dbContext.UserGamificationStats
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return stats is null
            ? ProgressErrors.UserGamificationStatsNotFound()
            : stats;
    }
}
