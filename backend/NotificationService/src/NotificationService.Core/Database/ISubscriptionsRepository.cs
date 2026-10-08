using System.Linq.Expressions;
using NotificationService.Domain.Subscriptions;

namespace NotificationService.Core.Database;

/// <summary>
/// Репозиторий подписок пользователей / Subscriptions repository.
/// Используется rule-контекстом для side-effect'ов (автоподписка при enrollment).
/// </summary>
public interface ISubscriptionsRepository
{
    Task<bool> ExistsBy(
        Expression<Func<Subscription, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    /// Возвращает первую подписку по предикату или <c>null</c>. Используется
    /// idempotent-путём в <c>Subscribe</c> и для ownership-фильтра в <c>Unsubscribe</c>
    /// (одним запросом вместо ExistsBy+ListBy / ListBy+foreach).
    /// </summary>
    Task<Subscription?> GetBy(
        Expression<Func<Subscription, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    /// Возвращает userIds, у которых УЖЕ есть подписка на сущность <c>(entityType, entityId)</c>
    /// среди указанного <paramref name="userIds"/> набора. Batch-аналог
    /// <c>ExistsBy</c> — устраняет N+1 при fan-out subscribe (issue #230, PERF-1).
    /// </summary>
    Task<IReadOnlySet<Guid>> GetSubscribedUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        string entityType,
        Guid entityId,
        CancellationToken ct = default);

    Task AddAsync(Subscription subscription, CancellationToken ct = default);

    /// <summary>
    /// Bulk add для fan-out subscribe (issue #80). Caller гарантирует, что
    /// дубликатов нет (см. <see cref="GetSubscribedUserIdsAsync"/>).
    /// </summary>
    Task AddRangeAsync(IReadOnlyCollection<Subscription> subscriptions, CancellationToken ct = default);

    Task<IReadOnlyList<Subscription>> ListBy(
        Expression<Func<Subscription, bool>> predicate,
        CancellationToken ct = default);

    Task RemoveAsync(SubscriptionId id, CancellationToken ct = default);
}
