using System.Linq.Expressions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.FeedbackRatings;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class AiFeedbackRatingsRepository : IAiFeedbackRatingsRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public AiFeedbackRatingsRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(AiFeedbackRating rating, CancellationToken ct = default) =>
        await _dbContext.AiFeedbackRatings.AddAsync(rating, ct);

    public async Task<Result<AiFeedbackRating, Error>> GetByAsync(
        Expression<Func<AiFeedbackRating, bool>> predicate,
        CancellationToken ct = default)
    {
        AiFeedbackRating? rating = await _dbContext.AiFeedbackRatings.FirstOrDefaultAsync(predicate, ct);
        return rating is null
            ? TrainerServiceErrors.FeedbackRating.NotFound()
            : rating;
    }

    public Task<bool> ExistsAsync(
        Expression<Func<AiFeedbackRating, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.AiFeedbackRatings.AnyAsync(predicate, ct);
}
