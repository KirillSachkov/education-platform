using System.Linq.Expressions;
using AccessService.Domain.Onboarding;

namespace AccessService.Core.Database;

public interface IUserPlanOnboardingsRepository
{
    Task AddAsync(UserPlanOnboarding onboarding, CancellationToken ct = default);

    Task<UserPlanOnboarding?> GetAsync(Guid userId, Guid planId, CancellationToken ct = default);

    Task<IReadOnlyList<UserPlanOnboarding>> GetManyByAsync(
        Expression<Func<UserPlanOnboarding, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid userId, Guid planId, CancellationToken ct = default);
}
