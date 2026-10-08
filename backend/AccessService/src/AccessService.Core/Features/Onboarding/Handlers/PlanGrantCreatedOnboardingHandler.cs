using AccessService.Core.Database;
using AccessService.Domain;
using AccessService.Domain.Onboarding;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace AccessService.Core.Features.Onboarding.Handlers;

/// <summary>
///     Wolverine consumer для <see cref="PlanGrantCreated"/>. При получении нового grant'а
///     если у плана включён онбординг и для пары (user, plan) ещё нет state'а —
///     создаёт <see cref="UserPlanOnboarding"/>. Идемпотентно: повторный grant того же
///     плана пользователю не пере-создаёт state. Для completed онбординга — no-op.
/// </summary>
public static class PlanGrantCreatedOnboardingHandler
{
    public static async Task HandleAsync(
        PlanGrantCreated message,
        IPlansRepository plans,
        IPlanOnboardingFlowsRepository flows,
        IUserPlanOnboardingsRepository onboardings,
        ITransactionManager transactions,
        TimeProvider time,
        ILogger<UserPlanOnboarding> logger,
        CancellationToken ct)
    {
        Guid? onboardingPlanId = await ResolveOnboardingPlanIdAsync(message, plans, logger, ct);
        if (onboardingPlanId is null)
        {
            return;
        }

        Result<PlanOnboardingFlow, Error> flowResult = await flows.GetByAsync(
            f => f.PlanId == onboardingPlanId.Value, ct);
        if (flowResult.IsFailure)
        {
            return; // нет flow → онбординг не настроен → noop.
        }

        PlanOnboardingFlow flow = flowResult.Value;
        if (!flow.IsEnabled || flow.Steps.Count == 0)
        {
            return;
        }

        bool exists = await onboardings.ExistsAsync(message.UserId, onboardingPlanId.Value, ct);
        if (exists)
        {
            return; // идемпотентно
        }

        DateTimeOffset now = time.GetUtcNow();
        UserPlanOnboarding onboarding = UserPlanOnboarding.Start(message.UserId, onboardingPlanId.Value, now);

        Guid firstStepId = flow.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .First()
            .Id;
        onboarding.SetCurrentStep(firstStepId);

        await onboardings.AddAsync(onboarding, ct);
        UnitResult<Error> saveResult = await transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            throw saveResult.Error.AsTransient().ToException();
        }

        logger.LogInformation(
            "Onboarding started: user={UserId} grantPlan={GrantPlanId} onboardingPlan={OnboardingPlanId} firstStep={StepId}",
            message.UserId, message.PlanId, onboardingPlanId.Value, firstStepId);
    }

    private static async Task<Guid?> ResolveOnboardingPlanIdAsync(
        PlanGrantCreated message,
        IPlansRepository plans,
        ILogger logger,
        CancellationToken ct)
    {
        Result<Plan, Error> planResult = await plans.GetByAsync(p => p.Id == message.PlanId, ct);
        if (planResult.IsFailure)
        {
            return message.PlanId;
        }

        Plan plan = planResult.Value;
        if (!plan.IsTrial)
        {
            return plan.Id;
        }

        IReadOnlyList<Plan> candidates = await plans.GetManyByAsync(
            p => p.AuthorId == plan.AuthorId
              && p.Tier == PlanTier.FULL_ALL
              && p.TrialDurationDays == null
              && p.ArchivedAt == null
              && p.IsActive
              && p.IsPublic,
            ct);

        Plan? canonicalFullAccess = candidates
            .OrderByDescending(p => p.IsHighlighted)
            .ThenBy(p => p.DisplayOrder)
            .ThenBy(p => p.CreatedAt)
            .FirstOrDefault();

        if (canonicalFullAccess is null)
        {
            logger.LogWarning(
                "Trial onboarding skipped: canonical FULL_ALL plan not found for author={AuthorId} trialPlan={TrialPlanId}",
                plan.AuthorId,
                plan.Id);
            return null;
        }

        return canonicalFullAccess.Id;
    }
}
