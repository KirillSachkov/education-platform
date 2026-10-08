namespace Shared.Messaging.IntegrationEvents.Access.Events;

/// <summary>
/// Publish'ится AccessService при полном (hard) удалении <c>Plan</c> — сам план и все его
/// дочерние / ссылающиеся строки в схеме <c>access</c> уже снесены в той же транзакции.
/// Consumer'ы должны убрать собственные ссылки на план (TelegramBotService — отвязать
/// chat-binding'и плана). Hard-delete разрешён только для «пустого» плана (без платных
/// заказов и активных грантов), поэтому снимать доступ не нужно — активных грантов нет.
/// </summary>
/// <param name="PlanId">ID удалённого плана.</param>
/// <param name="AuthorId">ID автора плана (для scoping / логов у consumer'ов).</param>
public sealed record PlanHardDeleted(
    Guid PlanId,
    Guid AuthorId);
