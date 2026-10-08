using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>access.events / plan_grant.expired</c> → самому пользователю (#687).
///
/// AccessService (фоновый <c>ExpiredGrantsSweeper</c>) перевёл time-limited grant в EXPIRED
/// по истечении TTL и опубликовал <c>PlanGrantExpired</c>. В отличие от pre-expiry напоминания
/// (<c>TrialExpiryApproachingHandler</c>, которое приходит ДО потери доступа), это уведомление
/// приходит ПОСЛЕ — когда доступ уже снят — и зовёт продлить: при доплате зачтётся уже
/// оплаченное. Клик ведёт в каталог планов (<c>/pricing</c>) через <c>PlatformLinkBuilder</c>.
///
/// Событие <see cref="PlanGrantExpired"/> не несёт <c>PlanName</c> (в отличие от
/// <c>TrialExpiryApproaching</c>), поэтому человекочитаемое описание истёкшего доступа строится
/// из <c>PlanTier</c> — тем же tier-фоллбэком, что и <c>PlanGrantReceivedHandler.BuildSummary</c>.
/// Внешних lookup'ов нет — событие самодостаточно. Идемпотентность per-grant через <c>GrantId</c>.
/// Каналы InApp + Telegram + Email (владелец явно хочет все три).
///
/// <para><c>plan_grant.revoked</c> сознательно НЕ консьюмится: revoke покрывает admin-revoke /
/// refund, где текст «продлите доступ — зачтётся оплаченное» вводил бы в заблуждение. Приоритет
/// задачи — истечение по TTL.</para>
/// </summary>
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
