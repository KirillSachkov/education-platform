using AccessService.Core.Database;
using AccessService.Domain.Onboarding;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Telegram.Events;

namespace AccessService.Core.Features.Onboarding.Handlers;

/// <summary>
///     Wolverine consumer for <see cref="ChatMemberConfirmed"/> (epic #397).
///     When a user's join into a plan's Telegram community group is approved,
///     mark the pending <c>TELEGRAM</c> step in that user's active onboarding for
///     the plan as completed + advance the cursor.
///
///     Mirrors <see cref="VcsInstallationCreatedHandler"/> exactly (same
///     load/guard/complete/advance/save). Idempotent: <see cref="UserPlanOnboarding.CompleteStep"/>
///     is a no-op if the step is already completed, and a missing onboarding / TELEGRAM
///     step is a silent no-op. The step stays skippable — we do NOT auto-finalize the
///     onboarding (the user explicitly clicks «Завершить» in the UI).
/// </summary>
public static class ChatMembershipConfirmedHandler
{
    public static async Task HandleAsync(
        ChatMemberConfirmed message,
        IUserPlanOnboardingsRepository onboardings,
        IPlanOnboardingFlowsRepository flows,
        ITransactionManager transactions,
        ILogger<UserPlanOnboarding> logger,
        CancellationToken ct)
    {
        UserPlanOnboarding? onboarding = await onboardings.GetAsync(
            message.PlatformUserId, message.PlanId, ct);
        if (onboarding is null || onboarding.IsCompleted)
        {
            return;
        }

        Result<PlanOnboardingFlow, Error> flowResult = await flows.GetByAsync(
            f => f.PlanId == message.PlanId, ct);
        if (flowResult.IsFailure)
        {
            return;
        }

        PlanOnboardingFlow flow = flowResult.Value;
        PlanOnboardingStep? telegramStep = flow.Steps
            .FirstOrDefault(s => s.Type == PlanOnboardingStepType.TELEGRAM);
        if (telegramStep is null)
        {
            return;
        }

        UnitResult<Error> completion = onboarding.CompleteStep(telegramStep.Id);
        if (completion.IsFailure)
        {
            return;
        }

        IReadOnlyList<Guid> orderedStepIds = flow.Steps
            .OrderBy(s => s.SortOrder.Value, StringComparer.Ordinal)
            .Select(s => s.Id)
            .ToList();
        onboarding.AdvanceTo(orderedStepIds);

        UnitResult<Error> saveResult = await transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            throw saveResult.Error.AsTransient().ToException();
        }

        logger.LogInformation(
            "Auto-completed TELEGRAM onboarding step for user={UserId} plan={PlanId} after chat-member confirmed (chat={ChatId})",
            message.PlatformUserId, message.PlanId, message.TelegramChatId);
    }
}
