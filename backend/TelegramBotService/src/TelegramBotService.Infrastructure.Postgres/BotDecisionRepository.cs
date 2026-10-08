using Microsoft.EntityFrameworkCore;
using TelegramBotService.Core.Database;
using TelegramBotService.Domain.Audit;

namespace TelegramBotService.Infrastructure.Postgres;

public sealed class BotDecisionRepository : IBotDecisionRepository
{
    private readonly TelegramBotDbContext _dbContext;

    public BotDecisionRepository(TelegramBotDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(BotDecision decision, CancellationToken cancellationToken = default)
    {
        await _dbContext.BotDecisions.AddAsync(decision, cancellationToken);
    }

    public async Task<int> DeleteOlderThanAsync(
        DateTime cutoff, int batchSize, CancellationToken cancellationToken = default)
    {
        // ExecuteDeleteAsync через LINQ — без in-memory tracking.
        // Для batch-cleanup'а large-table'ов можно через ROWNUM/limited DELETE — но 5000 в день
        // (наш масштаб) укладывается в один UPDATE без разбивки.
        return await _dbContext.BotDecisions
            .Where(x => x.CreatedAt < cutoff)
            .Take(batchSize)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
