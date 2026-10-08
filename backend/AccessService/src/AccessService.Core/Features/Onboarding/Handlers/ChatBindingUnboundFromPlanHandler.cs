using AccessService.Core.Database;
using AccessService.Domain.Onboarding;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Telegram.Events;

namespace AccessService.Core.Features.Onboarding.Handlers;

/// <summary>
///     Wolverine consumer для <see cref="ChatBindingUnboundFromPlan"/>. Removes
///     TELEGRAM step из onboarding flow ТОЛЬКО когда у плана не осталось ни одного
///     chat-binding'а (<see cref="ChatBindingUnboundFromPlan.RemainingBindingsCount"/>
///     == 0). Если ещё есть binding'и — no-op (шаг остаётся).
///
///     Источник правды для count'а — TelegramBotService UnbindChatHandler, который
///     считает binding'и до удаления и шлёт правильный count в event payload.
/// </summary>
public static class ChatBindingUnboundFromPlanHandler
{
    public static async Task HandleAsync(
        ChatBindingUnboundFromPlan message,
        IPlanOnboardingFlowsRepository flows,
        ITransactionManager transactions,
        TimeProvider time,
        ILogger<PlanOnboardingFlow> logger,
        CancellationToken ct)
    {
        if (message.RemainingBindingsCount > 0)
        {
            // У плана ещё есть chat-binding'и — TELEGRAM step должен оставаться.
            return;
        }

        Result<PlanOnboardingFlow, Error> flowResult = await flows.GetByAsync(
            f => f.PlanId == message.PlanId, ct);
        if (flowResult.IsFailure || !flowResult.Value.IsEnabled)
        {
            return;
        }

        flowResult.Value.RemoveAutoStep(PlanOnboardingStepType.TELEGRAM, time.GetUtcNow());
        UnitResult<Error> saveResult = await transactions.SaveChangesAsync(ct);
        if (saveResult.IsFailure)
        {
            throw saveResult.Error.AsTransient().ToException();
        }

        logger.LogInformation(
            "Removed TELEGRAM step from onboarding flow plan={PlanId} after last chat-binding {BindingId} unbound",
            message.PlanId, message.BindingId);
    }
}
