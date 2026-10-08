using AssignmentReviewService.Domain.AiSettings;

namespace AssignmentReviewService.Core.Database;

public interface IAiModelSettingsRepository
{
    Task<AiModelSettings?> GetSingletonAsync(CancellationToken ct = default);

    Task AddAsync(AiModelSettings settings, CancellationToken ct = default);
}
