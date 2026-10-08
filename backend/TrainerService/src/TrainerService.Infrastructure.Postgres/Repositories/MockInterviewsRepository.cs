using System.Linq.Expressions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.MockInterviews;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class MockInterviewsRepository : IMockInterviewsRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public MockInterviewsRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(MockInterview mockInterview, CancellationToken ct = default) =>
        await _dbContext.MockInterviews.AddAsync(mockInterview, ct);

    public async Task<Result<MockInterview, Error>> GetByAsync(
        Expression<Func<MockInterview, bool>> predicate,
        CancellationToken ct = default)
    {
        MockInterview? mockInterview = await _dbContext.MockInterviews
            .Include(m => m.Questions)
            .FirstOrDefaultAsync(predicate, ct);
        return mockInterview is null
            ? TrainerServiceErrors.MockInterview.NotFound(Guid.Empty)
            : mockInterview;
    }

    public async Task<IReadOnlyList<MockInterview>> GetManyByAsync(
        Expression<Func<MockInterview, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.MockInterviews
            .Include(m => m.Questions)
            .Where(predicate)
            .ToListAsync(ct);

    public Task<int?> GetMaxSortIndexAsync(CancellationToken ct = default) =>
        _dbContext.MockInterviews
            .Select(m => (int?)m.SortIndex)
            .MaxAsync(ct);

    public Task<bool> ExistsAsync(
        Expression<Func<MockInterview, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.MockInterviews.AnyAsync(predicate, ct);
}
