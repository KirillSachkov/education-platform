using System.Linq.Expressions;
using ProgressService.Domain.Gamification;

namespace ProgressService.Core.Abstractions;

public interface IUserStatsRepository
{
    Task AddAsync(UserGamificationStats stats, CancellationToken cancellationToken = default);

    Task<Result<UserGamificationStats, Error>> GetByAsync(
        Expression<Func<UserGamificationStats, bool>> predicate,
        CancellationToken cancellationToken = default);
}
