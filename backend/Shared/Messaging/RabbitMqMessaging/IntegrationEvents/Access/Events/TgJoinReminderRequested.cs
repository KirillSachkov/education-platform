namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService когда у активного community-гранта (план с привязанными
/// Telegram-чатами, <c>EnrollmentGrantsMembership=true</c>) пользователь ещё НЕ вступил
/// в группу — чтобы гарантированно довести его до вступления out-of-band (#616). Стадии:
/// <list type="bullet">
///   <item><c>INITIAL</c> — сразу на <c>plan_grant.created</c> (on-grant handler);</item>
///   <item><c>REMINDER_1</c> — ≈ через 2 дня, если так и не вступил (<c>TgJoinReminderSweeper</c>);</item>
///   <item><c>REMINDER_2</c> — ≈ через 9 дней (2+7), последнее напоминание; дальше стоп.</item>
/// </list>
/// Consumer (NotificationService) шлёт уведомление по каналам, которые реально достают
/// пользователя: привязан Telegram → InApp + Telegram-DM с кнопкой; не привязан →
/// InApp + Email (единственный out-of-band канал). CTA ведёт на флоу «привязка → вступление».
/// На <c>INITIAL</c> email НЕ шлётся (чтобы не спамить тех, кто вступит сразу) — только с
/// <c>REMINDER_1</c>. См. <see cref="TgJoinReminderStages"/> для значений стадии.
/// </summary>
/// <param name="UserId">ID платформенного пользователя, которого зовём в группу.</param>
/// <param name="PlanId">ID плана с привязанными чатами.</param>
/// <param name="GrantId">ID <c>PlanGrant</c>, давшего community-доступ (idempotency scope для consumer'а).</param>
/// <param name="PlanName">Отображаемое имя плана (<c>Plan.DisplayName</c>) для текста уведомления.</param>
/// <param name="Stage">Стадия нуджа (<see cref="TgJoinReminderStages"/>): <c>INITIAL | REMINDER_1 | REMINDER_2</c>.</param>
/// <param name="OccurredAt">Время публикации (UTC).</param>
public sealed record TgJoinReminderRequested(
    Guid UserId,
    Guid PlanId,
    Guid GrantId,
    string PlanName,
    string Stage,
    DateTimeOffset OccurredAt);

/// <summary>
/// Стадии нуджа на вступление в Telegram-группу (<see cref="TgJoinReminderRequested.Stage"/>).
/// Общий контракт между AccessService (publisher) и NotificationService (consumer), чтобы
/// не плодить magic-string'и в обоих сервисах.
/// </summary>
public static class TgJoinReminderStages
{
    /// <summary>Мгновенный нудж на момент выдачи гранта. Каналы: InApp + Telegram-если-привязан (без email).</summary>
    public const string Initial = "INITIAL";

    /// <summary>Первое напоминание (≈ T+2 дня), если не вступил. Каналы: InApp + Email + Telegram-если-привязан.</summary>
    public const string Reminder1 = "REMINDER_1";

    /// <summary>Второе и последнее напоминание (≈ T+9 дней). Каналы: те же, что у <see cref="Reminder1"/>.</summary>
    public const string Reminder2 = "REMINDER_2";
}
