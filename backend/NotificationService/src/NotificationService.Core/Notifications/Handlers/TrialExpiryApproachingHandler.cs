using System.Globalization;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>access.events / trial.expiry_approaching</c> → самому пользователю (#580).
///
/// Пробный (trial) доступ скоро истекает: AccessService (фоновый <c>TrialExpiryReminderSweeper</c>)
/// шлёт напоминание доплатить до полного доступа — уплаченное за пробный месяц засчитывается в
/// grace-окне. Событие самодостаточно (<c>PlanName</c> + <c>ExpiresAt</c> в payload), внешних
/// lookup'ов не требует. correlation = <c>GrantId</c> — повторная доставка по тому же гранту не
/// создаёт дубль. Клик ведёт на каталог планов (через <c>PlatformLinkBuilder</c>), где оформляется
/// апгрейд. Каналы InApp + Telegram (мягкое напоминание, без почтового спама).
/// </summary>
public sealed class TrialExpiryApproachingHandler
{
    private readonly INotificationDispatcher _dispatcher;

    public TrialExpiryApproachingHandler(INotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task Handle(TrialExpiryApproaching evt, CancellationToken ct)
    {
        string planName = string.IsNullOrWhiteSpace(evt.PlanName) ? "пробный доступ" : evt.PlanName.Trim();
        string expiresAt = evt.ExpiresAt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.TrialExpiryApproaching,
            recipientUserId: evt.UserId,
            correlationId: evt.GrantId,
            args: TemplateArgs.Of(
                ("planName", planName),
                ("expiresAt", expiresAt)),
            payload: new { grantId = evt.GrantId, planId = evt.PlanId });

        await _dispatcher.DispatchAsync(request, ct);
    }
}
