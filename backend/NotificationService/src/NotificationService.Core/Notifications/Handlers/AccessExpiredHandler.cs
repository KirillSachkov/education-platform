using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>Existing grant expired. The notification points to access history without offering a new purchase.</summary>
public sealed class AccessExpiredHandler
{
    private readonly INotificationDispatcher _dispatcher;

    public AccessExpiredHandler(INotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task Handle(PlanGrantExpired evt, CancellationToken ct)
    {
        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.AccessExpired,
            recipientUserId: evt.UserId,
            correlationId: evt.GrantId,
            args: TemplateArgs.Of(("accessSummary", BuildAccessSummary(evt.PlanTier))),
            payload: new { grantId = evt.GrantId, planId = evt.PlanId });

        await _dispatcher.DispatchAsync(request, ct);
    }

    /// <summary>
    /// Человекочитаемое описание истёкшего доступа из <c>PlanTier</c> wire-контракта.
    /// Зеркалит <c>PlanGrantReceivedHandler.BuildSummary</c>, но прошедшим временем
    /// и без <c>PlanName</c> (его в <see cref="PlanGrantExpired"/> нет).
    /// </summary>
    private static string BuildAccessSummary(string? planTier) => planTier switch
    {
        PlanTierNames.FULL_ALL => "Срок полного доступа к .NET Fullstack",
        PlanTierNames.LEARN_ALL => "Срок доступа ко всем материалам .NET Fullstack",
        PlanTierNames.COURSE => "Срок доступа к курсу",
        _ => "Срок вашего доступа",
    };
}