using System.Globalization;
using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>Existing trial expiry reminder. Purchases are closed; the notification preserves the original expiry date.</summary>
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