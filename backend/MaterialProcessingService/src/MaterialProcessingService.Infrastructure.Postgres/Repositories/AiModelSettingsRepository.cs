using Microsoft.EntityFrameworkCore;
using MaterialProcessingService.Core.Repositories;
using MaterialProcessingService.Domain.AiSettings;

namespace MaterialProcessingService.Infrastructure.Postgres.Repositories;

public sealed class AiModelSettingsRepository : IAiModelSettingsRepository
{
    private readonly MaterialProcessingServiceDbContext _db;

    public AiModelSettingsRepository(MaterialProcessingServiceDbContext db)
    {
        _db = db;
    }

    public async Task<AiModelSettings?> GetAsync(
        bool asNoTracking = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<AiModelSettings> query = _db.AiModelSettings;
        if (asNoTracking)
            query = query.AsNoTracking();

        return await query.FirstOrDefaultAsync(x => x.Id == AiModelSettings.SINGLETON_ID, cancellationToken);
    }

    public async Task AddAsync(AiModelSettings settings, CancellationToken cancellationToken = default)
    {
        await _db.AiModelSettings.AddAsync(settings, cancellationToken);
    }
}
