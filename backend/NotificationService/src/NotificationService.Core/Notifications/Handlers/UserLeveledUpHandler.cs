using NotificationService.Core.Dispatching;
using NotificationService.Core.Templates;
using NotificationService.Core.Templates.Catalog;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace NotificationService.Core.Notifications.Handlers;

/// <summary>
/// <c>progress.events / user.leveled_up</c> → самому пользователю (#555).
///
/// Поздравление с повышением gamification-уровня. Событие самодостаточно (номер уровня + XP уже
/// в payload), внешних lookup'ов не требует. На сайте поверх inbox-записи фронт показывает модалку
/// с конфетти. correlation = детерминированный id из (userId, newLevel) — повторная доставка того
/// же level-up не создаёт дубль (один уровень достигается один раз). Payload несёт newLevel/totalXp
/// для фронтовой модалки.
/// </summary>
public sealed class UserLeveledUpHandler
{
    private readonly INotificationDispatcher _dispatcher;

    public UserLeveledUpHandler(INotificationDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public async Task Handle(UserLeveledUp evt, CancellationToken ct)
    {
        // Детерминированный correlation: (userId × level-namespace-guid). Уникален в пределах
        // типа UserLeveledUp (тип входит в unique-индекс), уровень достигается один раз.
        // CorrelationIds.Combine XOR-симметричен — пара (userId, levelKey) намеренно уникальна:
        // НЕ переиспользовать тот же level-namespace-guid с другим Guid-доменом (см. CorrelationIds.cs).
        Guid levelKey = new(evt.NewLevel, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        Guid correlationId = CorrelationIds.Combine(evt.UserId, levelKey);

        NotificationRequest request = NotificationRequest.From(
            template: NotificationTemplates.UserLeveledUp,
            recipientUserId: evt.UserId,
            correlationId: correlationId,
            args: TemplateArgs.Of(
                ("newLevel", evt.NewLevel.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("levelLine", $"Ты уже набрал {evt.TotalXp.ToString(System.Globalization.CultureInfo.InvariantCulture)} XP.")),
            payload: new { newLevel = evt.NewLevel, previousLevel = evt.PreviousLevel, totalXp = evt.TotalXp });

        await _dispatcher.DispatchAsync(request, ct);
    }
}
