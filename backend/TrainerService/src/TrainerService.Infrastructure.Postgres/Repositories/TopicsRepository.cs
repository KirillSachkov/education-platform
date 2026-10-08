using System.Linq.Expressions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Topics;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class TopicsRepository : ITopicsRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public TopicsRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Topic topic, CancellationToken ct = default) =>
        await _dbContext.Topics.AddAsync(topic, ct);

    public Task RemoveAsync(Topic topic, CancellationToken ct = default)
    {
        _dbContext.Topics.Remove(topic);
        return Task.CompletedTask;
    }

    public async Task<Result<Topic, Error>> GetByAsync(
        Expression<Func<Topic, bool>> predicate,
        CancellationToken ct = default)
    {
        Topic? topic = await _dbContext.Topics.FirstOrDefaultAsync(predicate, ct);
        return topic is null
            ? TrainerServiceErrors.Topic.NotFound(Guid.Empty)
            : topic;
    }

    public async Task<IReadOnlyList<Topic>> GetManyByAsync(
        Expression<Func<Topic, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.Topics.Where(predicate).ToListAsync(ct);

    public Task<string?> GetMaxSortKeyAsync(CancellationToken ct = default) =>
        _dbContext.Topics
            .OrderByDescending(t => t.SortKey)
            .Select(t => t.SortKey)
            .FirstOrDefaultAsync(ct);

    public Task<bool> ExistsAsync(
        Expression<Func<Topic, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.Topics.AnyAsync(predicate, ct);
}
