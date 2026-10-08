namespace Shared.Messaging.IntegrationEvents.Telegram.Events;

/// <summary>
///     Publish'ится TelegramBotService после успешного approve'а chat_join_request юзера
///     в чат, привязанный к плану, на который у юзера есть active plan-grant. Один event
///     на каждый matched plan (чат может быть bound к нескольким планам). Consumer'ы
///     (AccessService) используют для подтверждения членства / онбординга.
/// </summary>
/// <param name="PlatformUserId">ID платформенного пользователя, чей join был одобрен.</param>
/// <param name="PlanId">ID плана, grant на который дал доступ в чат.</param>
/// <param name="TelegramChatId">Telegram chat id (negative for supergroup/channel).</param>
/// <param name="OccurredAt">Время approve'а (UTC).</param>
public sealed record ChatMemberConfirmed(
    Guid PlatformUserId,
    Guid PlanId,
    long TelegramChatId,
    DateTimeOffset OccurredAt);
