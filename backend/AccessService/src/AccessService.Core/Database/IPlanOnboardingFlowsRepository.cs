using System.Linq.Expressions;
using AccessService.Domain.Onboarding;

namespace AccessService.Core.Database;

public interface IPlanOnboardingFlowsRepository
{
    Task AddAsync(PlanOnboardingFlow flow, CancellationToken ct = default);

    Task<Result<PlanOnboardingFlow, Error>> GetByAsync(
        Expression<Func<PlanOnboardingFlow, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<PlanOnboardingFlow, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlanOnboardingFlow>> GetManyByAsync(
        Expression<Func<PlanOnboardingFlow, bool>> predicate,
        CancellationToken ct = default);
}
