using System.Linq.Expressions;
using AccessService.Core.Database;
using AccessService.Domain;

namespace AccessService.Infrastructure.Postgres;

internal sealed class PlansRepository : IPlansRepository
{
    private readonly AccessServiceDbContext _db;

    public PlansRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(Plan plan, CancellationToken ct = default) =>
        await _db.Plans.AddAsync(plan, ct);

    public async Task<Result<Plan, Error>> GetByAsync(
        Expression<Func<Plan, bool>> predicate,
        CancellationToken ct = default)
    {
        // Eager-load the bundle nav-collection explicitly (rule 3 — don't rely on AutoInclude alone).
        Plan? plan = await _db.Plans.Include(p => p.Courses).FirstOrDefaultAsync(predicate, ct);
        return plan is null ? AccessErrors.PlanNotFound() : plan;
    }

    public async Task<IReadOnlyList<Plan>> GetManyByAsync(
        Expression<Func<Plan, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.Plans.Include(p => p.Courses).Where(predicate).ToListAsync(ct);

    public async Task<bool> ExistsAsync(
        Expression<Func<Plan, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.Plans.AnyAsync(predicate, ct);

    public void Remove(Plan plan) => _db.Plans.Remove(plan);
}
