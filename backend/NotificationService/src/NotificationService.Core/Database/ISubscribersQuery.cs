namespace NotificationService.Core.Database;

public sealed record SubscriberPage(
    IReadOnlyList<Guid> UserIds,
    Guid? NextAfterUserId);

/// <summary>
/// Резолвит подписчиков сущности bounded keyset-страницами. Используется правилами,
/// которые раскручивают широкое событие (material.published) в набор получателей.
/// </summary>
public interface ISubscribersQuery
{
    Task<int> CountByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct = default);

    Task<SubscriberPage> ByEntityPageAsync(
        string entityType,
        Guid entityId,
        Guid? afterUserId,
        int limit,
        CancellationToken ct = default);
}
