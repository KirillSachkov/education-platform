using TelegramBotService.Domain.Audit;

namespace TelegramBotService.Core.Database;

public interface IBotDecisionRepository
{
    Task AddAsync(BotDecision decision, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Удаляет записи старше <paramref name="cutoff"/> батчами по <paramref name="batchSize"/>.
    ///     Возвращает количество удалённых.
    /// </summary>
    Task<int> DeleteOlderThanAsync(DateTime cutoff, int batchSize, CancellationToken cancellationToken = default);
}
