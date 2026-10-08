using AccessService.Core.Database;
using AccessService.Domain;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AccessService.Core.Features.Plans;

public static class CanonicalTelegramPlanResolver
{
    public static async Task<Result<Guid?, Error>> ResolveAsync(
        Guid planId,
        IPlansRepository plans,
        CancellationToken cancellationToken)
    {
        Result<Plan, Error> planResult = await plans.GetByAsync(
            plan => plan.Id == planId,
            cancellationToken);
        if (planResult.IsFailure)
            return planResult.Error;

        return await ResolveAsync(planResult.Value, plans, cancellationToken);
    }

    public static async Task<Guid?> ResolveAsync(
        Plan plan,
        IPlansRepository plans,
        CancellationToken cancellationToken)
    {
        if (!plan.IsTrial)
            return plan.Id;

        IReadOnlyList<Plan> candidates = await plans.GetManyByAsync(
            candidate => candidate.AuthorId == plan.AuthorId
                         && candidate.Tier == PlanTier.FULL_ALL
                         && candidate.TrialDurationDays == null
                         && candidate.ArchivedAt == null
                         && candidate.IsActive
                         && candidate.IsPublic,
            cancellationToken);

        return candidates
            .OrderByDescending(candidate => candidate.IsHighlighted)
            .ThenBy(candidate => candidate.DisplayOrder)
            .ThenBy(candidate => candidate.CreatedAt)
            .ThenBy(candidate => candidate.Id)
            .Select(candidate => (Guid?)candidate.Id)
            .FirstOrDefault();
    }
}
