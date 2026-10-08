using System.Linq.Expressions;
using AccessService.Domain;

namespace AccessService.Core.Database;

public interface IOrdersRepository
{
    /// <summary>
    /// Tries to acquire a PostgreSQL session advisory lock for one recurring grant.
    /// The returned lease must be disposed; <c>null</c> means another replica owns it.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireRenewalLockAsync(
        Guid grantId,
        CancellationToken ct = default);

    Task AddAsync(Order order, CancellationToken ct = default);

    Task<Result<Order, Error>> GetByAsync(
        Expression<Func<Order, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<Order>> GetManyByAsync(
        Expression<Func<Order, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<Order, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    /// Admin-search'ит заказы по фильтрам с пагинацией. Используется
    /// <c>GET /access/admin/orders</c>. Сортировка — по <c>created_at DESC</c>
    /// (новые сверху). Возвращает items + total для UI-пагинации.
    /// </summary>
    Task<(IReadOnlyList<Order> Items, int Total)> SearchAsync(
        OrderStatus? status,
        Guid? userId,
        Guid? planId,
        DateTimeOffset? createdFrom,
        DateTimeOffset? createdTo,
        string? correlationId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// Возвращает PENDING заказы данного провайдера старше <paramref name="minAgeCutoff"/>
    /// независимо от наличия <c>ExternalProviderRef</c>, отсортированные по
    /// <c>CreatedAt</c> (старые первыми — fairness), c hard-cap'ом
    /// <paramref name="limit"/> на DB-уровне. Пустой ref восстанавливается через CheckOrder.
    /// Используется <see cref="Features.Billing.Reconciliation.PendingOrderReconciliationService"/>.
    /// </summary>
    Task<IReadOnlyList<Order>> GetPendingForReconciliationAsync(
        string provider,
        DateTimeOffset minAgeCutoff,
        DateTimeOffset retryCutoff,
        int limit,
        CancellationToken ct = default);

    /// <summary>
    /// Возвращает уже PAID родительские subscription-заказы без RebillId. Это recovery-путь
    /// для потерянного AUTHORIZED webhook после того, как CONFIRMED уже выпустил grant.
    /// </summary>
    Task<IReadOnlyList<Order>> GetPaidSubscriptionsWithoutRebillAsync(
        string provider,
        DateTimeOffset minAgeCutoff,
        DateTimeOffset retryCutoff,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<Order>> GetPaidRenewalsForRefundReconciliationAsync(
        string provider,
        DateTimeOffset createdAfter,
        DateTimeOffset retryCutoff,
        int limit,
        CancellationToken ct = default);
}
