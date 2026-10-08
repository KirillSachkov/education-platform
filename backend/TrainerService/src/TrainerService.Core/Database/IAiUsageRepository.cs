using TrainerService.Domain.AiUsage;

namespace TrainerService.Core.Database;

/// <summary>
/// Append-only лоджер AI-использования (#614 C1). Только запись — чтение/агрегации (квоты — C2) ещё нет.
/// </summary>
public interface IAiUsageRepository
{
    Task AddAsync(AiUsageRecord record, CancellationToken ct = default);
}
