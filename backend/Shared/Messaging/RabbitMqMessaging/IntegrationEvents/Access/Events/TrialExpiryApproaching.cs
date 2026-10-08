namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService когда у активного trial-гранта приближается срок истечения
/// (фоновая задача <c>TrialExpiryReminderSweeper</c>, #580). Consumer'ы шлют пользователю
/// напоминание «пробный период скоро закончится — продлите доступ».
/// Published by AccessService when an active trial grant is approaching its TTL.
/// </summary>
/// <param name="GrantId">ID истекающего пробного PlanGrant.</param>
/// <param name="UserId">ID пользователя.</param>
/// <param name="PlanId">ID пробного плана.</param>
/// <param name="AuthorId">ID автора плана-владельца. Entitlement scope определяется планом/курсами.</param>
/// <param name="PlanName">Отображаемое имя плана (для текста уведомления).</param>
/// <param name="ExpiresAt">Время, когда пробный доступ истечёт (UTC).</param>
public sealed record TrialExpiryApproaching(
    Guid GrantId,
    Guid UserId,
    Guid PlanId,
    Guid AuthorId,
    string PlanName,
    DateTimeOffset ExpiresAt);
