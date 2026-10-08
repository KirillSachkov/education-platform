using System.Linq.Expressions;
using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.Onboarding;

namespace AccessService.Infrastructure.Postgres;

public sealed class PlanOnboardingFlowsRepository : IPlanOnboardingFlowsRepository
{
    private readonly AccessServiceDbContext _db;

    public PlanOnboardingFlowsRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(PlanOnboardingFlow flow, CancellationToken ct = default) =>
        await _db.PlanOnboardingFlows.AddAsync(flow, ct);

    public async Task<Result<PlanOnboardingFlow, Error>> GetByAsync(
        Expression<Func<PlanOnboardingFlow, bool>> predicate,
        CancellationToken ct = default)
    {
        // Eager-load Steps — без этого _steps collection приходит пустой даже при
        // EF AutoInclude конфигурации (зависит от backing-field discovery), и при
        // EnsureAutoStep / AddMarkdownStep change tracker помечает new step как
        // Modified вместо Added → DbUpdateConcurrencyException на UPDATE WHERE
        // id=<новый guid>. Явный Include — единственно надёжный путь.
        PlanOnboardingFlow? flow = await _db.PlanOnboardingFlows
            .Include(f => f.Steps)
            .FirstOrDefaultAsync(predicate, ct);
        if (flow is null)
        {
            return OnboardingErrors.FlowNotFound();
        }

        return flow;
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<PlanOnboardingFlow, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.PlanOnboardingFlows.AnyAsync(predicate, ct);

    public async Task<IReadOnlyList<PlanOnboardingFlow>> GetManyByAsync(
        Expression<Func<PlanOnboardingFlow, bool>> predicate,
        CancellationToken ct = default) =>
        await _db.PlanOnboardingFlows.AsNoTracking().Where(predicate).ToListAsync(ct);
}
