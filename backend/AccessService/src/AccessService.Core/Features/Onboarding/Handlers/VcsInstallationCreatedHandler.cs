using AccessService.Core.Database;
using AccessService.Domain.Onboarding;
using Core.Database;
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace AccessService.Core.Features.Onboarding.Handlers;

/// <summary>
///     Wolverine consumer for <see cref="VcsInstallationCreated"/> (issue #307).
///     When a user installs the AI-review GitHub App, mark every pending
///     <c>GITHUB_REVIEW_APP</c> step in their active onboardings as completed.
///
///     Idempotent: <see cref="UserPlanOnboarding.CompleteStep"/> is a no-op if the
///     step is already in <c>completedStepIds</c>. We do NOT auto-finalize the
///     onboarding here — the user explicitly clicks «Завершить» in the UI once
///     they've gone through all steps.
/// </summary>
public static class VcsInstallationCreatedHandler
{
    public static async Task HandleAsync(
        VcsInstallationCreated message,
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        ITransactionManager transactions,
        ILogger<UserPlanOnboarding> logger,
        CancellationToken ct)
    {
        IReadOnlyList<UserPlanOnboarding> active = await onboardings.GetManyByAsync(
            o => o.UserId == message.UserId && o.CompletedAt == null, ct);

        if (active.Count == 0)
        {
            return;
        }

        int touchedCount = 0;
        foreach (UserPlanOnboarding onboarding in active)
        {
            Result<PlanOnboardingFlow, Error> flowResult = await flows.GetByAsync(
                f => f.PlanId == onboarding.PlanId, ct);
            if (flowResult.IsFailure) continue;

            PlanOnboardingFlow flow = flowResult.Value;
            PlanOnboardingStep? githubReviewStep = flow.Steps
                .FirstOrDefault(s => s.Type == PlanOnboardingStepType.GITHUB_REVIEW_APP);
            if (githubReviewStep is null) continue;

            UnitResult<Error> completion = onboarding.CompleteStep(githubReviewStep.Id);
            if (completion.IsFailure) continue;

            IReadOnlyList<Guid> orderedStepIds = flow.Steps
                .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
                .Select(s => s.Id)
                .ToList();
            onboarding.AdvanceTo(orderedStepIds);
            touchedCount++;
        }

        if (touchedCount == 0) return;

        UnitResult<Error> saveResult = await transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            throw saveResult.Error.AsTransient().ToException();
        }

        logger.LogInformation(
            "Auto-completed GITHUB_REVIEW_APP step in {Count} onboarding(s) for user={UserId} after VCS install {InstallationId}",
            touchedCount, message.UserId, message.InstallationId);
    }
}
