using AccessService.Core.Database;
using AccessService.Domain.Onboarding;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Telegram.Events;

namespace AccessService.Core.Features.Onboarding.Handlers;

/// <summary>
///     Wolverine consumer для <see cref="ChatBindingBoundToPlan"/>. На bind tg-чата
///     к плану auto-ensure'ит TELEGRAM step в onboarding flow если flow.IsEnabled.
///     Идемпотентен (EnsureAutoStep сам проверяет существование).
/// </summary>
public static class ChatBindingBoundToPlanHandler
{
    public static async Task HandleAsync(
        ChatBindingBoundToPlan message,
        IPlanOnboardingFlowsRepository flows,
        ITransactionManager transactions,
        TimeProvider time,
        ILogger<PlanOnboardingFlow> logger,
        CancellationToken ct)
    {
        Result<PlanOnboardingFlow, Error> flowResult = await flows.GetByAsync(
            f => f.PlanId == message.PlanId, ct);
        if (flowResult.IsFailure || !flowResult.Value.IsEnabled)
        {
            return;
        }

        flowResult.Value.EnsureAutoStep(PlanOnboardingStepType.TELEGRAM, time.GetUtcNow());
        UnitResult<Error> saveResult = await transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            throw saveResult.Error.AsTransient().ToException();
        }

        logger.LogInformation(
            "Auto-ensured TELEGRAM step in onboarding flow plan={PlanId} after chat-binding {BindingId}",
            message.PlanId, message.BindingId);
    }
}
