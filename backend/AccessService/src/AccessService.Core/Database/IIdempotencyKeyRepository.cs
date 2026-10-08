using AccessService.Core.Domain;

namespace AccessService.Core.Database;

public interface IIdempotencyKeyRepository
{
    Task AddAsync(IdempotencyKey entry, CancellationToken ct = default);

    /// <summary>Returns an existing state scoped by both user and raw key.</summary>
    Task<IdempotencyKey?> GetByKeyAsync(
        Guid userId,
        string key,
        CancellationToken ct = default);

    Task<IdempotencyKey?> GetByOrderIdAsync(
        Guid orderId,
        CancellationToken ct = default);

    /// <summary>Удаляет записи старше cutoff. Используется cleanup-job'ом.</summary>
    Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default);
}
