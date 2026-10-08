using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using NotificationService.Domain.Notifications;
using Shared.Messaging.IntegrationEvents.Access.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>access.events / tg_join.reminder_requested</c> → самому пользователю (#616).
///
/// Нудж на вступление в Telegram-группу плана. AccessService шлёт событие в три стадии
/// (<see cref="TgJoinReminderStages"/>): INITIAL сразу на выдачу гранта, REMINDER_1/REMINDER_2 —
/// если юзер так и не вступил. Событие самодостаточно (PlanName в payload), внешних lookup'ов нет.
///
/// Per-стадийный набор каналов:
/// <list type="bullet">
///   <item><b>INITIAL</b> → только InApp. F1 invite-DM (<c>PlanGrantCreatedTelegramHandler</c>) уже
///   шлёт Telegram на момент гранта, поэтому Telegram-нудж здесь задублировал бы сообщение
///   привязанным; непривязанные Telegram всё равно не получают.</item>
///   <item><b>REMINDER_1 / REMINDER_2</b> → InApp + Telegram + Email (Telegram авто-фильтруется
///   при непривязанном TG → Email остаётся единственным out-of-band каналом).</item>
/// </list>
/// Telegram авто-фильтруется dispatcher'ом, когда у пользователя не привязан Telegram —
/// именно тогда Email (на reminder-стадиях) остаётся единственным out-of-band каналом. В этом
/// и смысл фичи: довести до вступления того, кто ещё не в боте.
///
/// Идемпотентность per-(grant, stage): correlation = <c>CorrelationIds.Combine(GrantId, stageGuid)</c> —
/// каждая стадия доставляется один раз на грант, повторная публикация той же стадии не дублирует.
/// </summary>
public sealed class TgJoinReminderRequestedHandler
{
    private readonly INotificationDispatcher _dispatcher;

    public TgJoinReminderRequestedHandler(INotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task Handle(TgJoinReminderRequested evt, CancellationToken ct)
    {
        string planName = string.IsNullOrWhiteSpace(evt.PlanName) ? "курс" : evt.PlanName.Trim();

        bool isInitial = string.Equals(evt.Stage, TgJoinReminderStages.Initial, StringComparison.Ordinal);

        // INITIAL → InApp only: F1 invite-DM уже покрывает Telegram на момент гранта, дубль не нужен.
        // REMINDER_* → InApp + Telegram + Email (Telegram авто-фильтруется при непривязанном TG).
        NotificationChannel channels = isInitial
            ? NotificationChannel.InApp
            : NotificationChannel.InApp | NotificationChannel.Telegram | NotificationChannel.Email;

        // Per-(grant, stage) идемпотентность: стадия → детерминированный namespace-guid.
        // CorrelationIds.Combine XOR-симметричен — пара (GrantId, stageGuid) уникальна в пределах
        // типа TelegramJoinReminder (тип входит в unique-индекс), namespace stageGuid не
        // переиспользуется с другим Guid-доменом.
        Guid correlationId = CorrelationIds.Combine(evt.GrantId, StageNamespace(evt.Stage));

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.TelegramJoinReminder,
            recipientUserId: evt.UserId,
            correlationId: correlationId,
            args: TemplateArgs.Of(("planName", planName)),
            payload: new { planId = evt.PlanId, grantId = evt.GrantId },
            channelsOverride: channels);

        await _dispatcher.DispatchAsync(request, ct);
    }

    /// <summary>
    /// Стабильный namespace-guid для стадии нуджа — делает correlation уникальным per-stage.
    /// Значения произвольны, но фиксированы: менять нельзя, иначе сломается идемпотентность
    /// для in-flight событий на момент deploy'а.
    /// </summary>
    private static Guid StageNamespace(string stage) => stage switch
    {
        TgJoinReminderStages.Initial => new Guid("a1000000-0000-0000-0000-000000000001"),
        TgJoinReminderStages.Reminder1 => new Guid("a1000000-0000-0000-0000-000000000002"),
        TgJoinReminderStages.Reminder2 => new Guid("a1000000-0000-0000-0000-000000000003"),
        _ => new Guid("a1000000-0000-0000-0000-0000000000ff"),
    };
}
