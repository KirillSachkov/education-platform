using System.Linq.Expressions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicBanks;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class TopicBanksRepository : ITopicBanksRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public TopicBanksRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(TopicBank bank, CancellationToken ct = default) =>
        await _dbContext.TopicBanks.AddAsync(bank, ct);

    public Task RemoveAsync(TopicBank bank, CancellationToken ct = default)
    {
        _dbContext.TopicBanks.Remove(bank);
        return Task.CompletedTask;
    }

    public async Task<Result<TopicBank, Error>> GetByAsync(
        Expression<Func<TopicBank, bool>> predicate,
        CancellationToken ct = default)
    {
        TopicBank? bank = await _dbContext.TopicBanks.FirstOrDefaultAsync(predicate, ct);
        return bank is null
            ? TrainerServiceErrors.Bank.NotFound(Guid.Empty)
            : bank;
    }

    public async Task<IReadOnlyList<TopicBank>> GetManyByAsync(
        Expression<Func<TopicBank, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.TopicBanks.Where(predicate).ToListAsync(ct);

    public Task<string?> GetMaxSortKeyAsync(Guid topicId, CancellationToken ct = default) =>
        _dbContext.TopicBanks
            .Where(b => b.TopicId == topicId)
            .OrderByDescending(b => b.SortKey)
            .Select(b => b.SortKey)
            .FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(
        Expression<Func<TopicBank, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.TopicBanks.AnyAsync(predicate, ct);
}
