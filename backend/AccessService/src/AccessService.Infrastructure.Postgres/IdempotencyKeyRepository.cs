using AccessService.Core.Database;
using AccessService.Core.Domain;

namespace AccessService.Infrastructure.Postgres;

internal sealed class IdempotencyKeyRepository : IIdempotencyKeyRepository
{
    private readonly AccessServiceDbContext _db;

    public IdempotencyKeyRepository(AccessServiceDbContext db) => _db = db;

    public async Task AddAsync(IdempotencyKey entry, CancellationToken ct = default) =>
        await _db.IdempotencyKeys.AddAsync(entry, ct);

    public async Task<IdempotencyKey?> GetByKeyAsync(
        Guid userId,
        string key,
        CancellationToken ct = default) =>
        await _db.IdempotencyKeys.AsNoTracking()
            .FirstOrDefaultAsync(k => k.UserId == userId && k.Key == key, ct);

    public async Task<IdempotencyKey?> GetByOrderIdAsync(
        Guid orderId,
        CancellationToken ct = default) =>
        await _db.IdempotencyKeys.FirstOrDefaultAsync(k => k.OrderId == orderId, ct);

    public async Task<int> DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct = default) =>
        await _db.IdempotencyKeys
            .Where(k => k.CreatedAt < cutoff
                && k.Status != IdempotencyKeyStatus.PROCESSING)
            .ExecuteDeleteAsync(ct);
}
