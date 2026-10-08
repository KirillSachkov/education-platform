using NotificationService.Domain.WebPush;

namespace NotificationService.Core.Database;

/// <summary>
/// Доступ к web-push подпискам устройств / Access to per-device web-push subscriptions.
/// </summary>
public interface IWebPushSubscriptionsRepository
{
    /// <summary>
    /// Идемпотентно создаёт/обновляет подписку по <c>endpoint</c> (глобально уникален).
    /// Повторная регистрация того же браузера обновляет ключи + <c>user_id</c> + <c>last_seen_at</c>.
    /// </summary>
    Task UpsertAsync(WebPushSubscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет подписку пользователя по endpoint'у (отписка устройства). Идемпотентно.
    /// </summary>
    Task RemoveByEndpointAsync(Guid userId, string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Удаляет протухший endpoint без привязки к пользователю — для prune'а на 404/410
    /// от push-сервиса (endpoint глобально уникален). Идемпотентно.
    /// </summary>
    Task PruneEndpointAsync(string endpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Все активные подписки пользователя (для доставки).
    /// </summary>
    Task<IReadOnlyList<WebPushSubscription>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Подмножество пользователей, у которых есть хотя бы одна подписка. Используется
    /// диспатчером чтобы не предлагать WebPush-канал юзерам без устройства (иначе на каждое
    /// уведомление писалась бы skipped-запись в <c>notification_deliveries</c>).
    /// </summary>
    Task<IReadOnlySet<Guid>> GetUserIdsWithSubscriptionsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}
