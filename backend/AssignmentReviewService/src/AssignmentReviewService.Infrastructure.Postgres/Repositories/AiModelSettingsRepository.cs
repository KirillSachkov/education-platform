using AssignmentReviewService.Core.Database;
using AssignmentReviewService.Domain.AiSettings;

namespace AssignmentReviewService.Infrastructure.Postgres.Repositories;

internal sealed class AiModelSettingsRepository : IAiModelSettingsRepository
{
    private readonly AssignmentReviewServiceDbContext _db;

    public AiModelSettingsRepository(AssignmentReviewServiceDbContext db) => _db = db;

    public Task<AiModelSettings?> GetSingletonAsync(CancellationToken ct = default) =>
        _db.AiModelSettings.FirstOrDefaultAsync(x => x.Id == AiModelSettings.SINGLETON_ID, ct);

    public async Task AddAsync(AiModelSettings settings, CancellationToken ct = default) =>
        await _db.AiModelSettings.AddAsync(settings, ct);
}
