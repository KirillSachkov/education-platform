namespace Shared.Messaging.IntegrationEvents.Telegram.Events;

/// <summary>
///     Publish'ится TelegramBotService после успешного bind tg-чата к плану.
///     Consumers: AccessService.PlanOnboarding (#68) — auto-ensure'ит TELEGRAM
///     onboarding step если flow.is_enabled.
/// </summary>
/// <param name="BindingId">ID созданного chat_binding.</param>
/// <param name="PlanId">ID плана.</param>
/// <param name="TelegramChatId">Telegram chat id (negative for supergroup/channel).</param>
public sealed record ChatBindingBoundToPlan(Guid BindingId, Guid PlanId, long TelegramChatId);
