using System.Linq.Expressions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.TopicMasteries;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class TopicMasteryRepository : ITopicMasteryRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public TopicMasteryRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(TopicMastery mastery, CancellationToken ct = default) =>
        await _dbContext.TopicMasteries.AddAsync(mastery, ct);

    public async Task<Result<TopicMastery, Error>> GetByAsync(
        Expression<Func<TopicMastery, bool>> predicate,
        CancellationToken ct = default)
    {
        TopicMastery? mastery = await _dbContext.TopicMasteries.FirstOrDefaultAsync(predicate, ct);
        return mastery is null
            ? TrainerServiceErrors.Mastery.NotFound(Guid.Empty, Guid.Empty)
            : mastery;
    }

    public async Task<IReadOnlyList<TopicMastery>> GetManyByAsync(
        Expression<Func<TopicMastery, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.TopicMasteries.Where(predicate).ToListAsync(ct);

    public Task<bool> ExistsAsync(
        Expression<Func<TopicMastery, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.TopicMasteries.AnyAsync(predicate, ct);
}
