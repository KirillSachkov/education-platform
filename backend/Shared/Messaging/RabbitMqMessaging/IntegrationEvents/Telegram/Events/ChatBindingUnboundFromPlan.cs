namespace Shared.Messaging.IntegrationEvents.Telegram.Events;

/// <summary>
///     Publish'ится TelegramBotService после успешного unbind tg-чата от плана.
///     Consumers: AccessService.PlanOnboarding (#68) — removes TELEGRAM step
///     ТОЛЬКО если <see cref="RemainingBindingsCount"/> == 0 (последний unbind).
/// </summary>
/// <param name="RemainingBindingsCount">
///     Сколько binding'ов осталось у плана ПОСЛЕ удаления текущего. Считается
///     в UnbindChatHandler на момент publish'а (count - 1).
/// </param>
public sealed record ChatBindingUnboundFromPlan(
    Guid BindingId,
    Guid PlanId,
    long TelegramChatId,
    int RemainingBindingsCount);
