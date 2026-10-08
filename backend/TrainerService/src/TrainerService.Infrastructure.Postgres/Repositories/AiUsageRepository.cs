using TrainerService.Core.Database;
using TrainerService.Domain.AiUsage;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class AiUsageRepository : IAiUsageRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public AiUsageRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(AiUsageRecord record, CancellationToken ct = default) =>
        await _dbContext.AiUsageRecords.AddAsync(record, ct);
}
