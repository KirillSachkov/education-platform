using AccessService.Core.Database;
using AccessService.Domain.Billing;

namespace AccessService.Infrastructure.Postgres;

internal sealed class BillingConfigRepository : IBillingConfigRepository
{
    private readonly AccessServiceDbContext _db;

    public BillingConfigRepository(AccessServiceDbContext db) => _db = db;

    public async Task<BillingConfig?> GetAsync(CancellationToken ct = default) =>
        await _db.BillingConfigs.FirstOrDefaultAsync(c => c.Id == BillingConfig.SingletonId, ct);

    public async Task AddAsync(BillingConfig config, CancellationToken ct = default) =>
        await _db.BillingConfigs.AddAsync(config, ct);
}
