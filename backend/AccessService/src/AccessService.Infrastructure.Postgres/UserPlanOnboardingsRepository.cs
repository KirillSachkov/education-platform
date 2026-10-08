using System.Linq.Expressions;
using AccessService.Core.Database;
using AccessService.Domain.Onboarding;

namespace AccessService.Infrastructure.Postgres;

public sealed class UserPlanOnboardingsRepository : IUserPlanOnboardingsRepository
{
    private readonly AccessServiceDbContext _db;

    public UserPlanOnboardingsRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(UserPlanOnboarding onboarding, CancellationToken ct = default) =>
        await _db.UserPlanOnboardings.AddAsync(onboarding, ct);

    public async Task<UserPlanOnboarding?> GetAsync(Guid userId, Guid planId, CancellationToken ct = default) =>
        await _db.UserPlanOnboardings.FirstOrDefaultAsync(
            u => u.UserId == userId && u.PlanId == planId, ct);

    public async Task<IReadOnlyList<UserPlanOnboarding>> GetManyByAsync(
        Expression<Func<UserPlanOnboarding, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.UserPlanOnboardings.Where(predicate).ToListAsync(ct);

    public async Task<bool> ExistsAsync(Guid userId, Guid planId, CancellationToken ct = default) =>
        await _db.UserPlanOnboardings.AnyAsync(
            u => u.UserId == userId && u.PlanId == planId, ct);
}
