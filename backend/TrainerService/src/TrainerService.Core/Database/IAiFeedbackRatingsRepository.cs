using System.Linq.Expressions;
using TrainerService.Domain.FeedbackRatings;

namespace TrainerService.Core.Database;

public interface IAiFeedbackRatingsRepository
{
    Task AddAsync(AiFeedbackRating rating, CancellationToken ct = default);

    Task<Result<AiFeedbackRating, Error>> GetByAsync(
        Expression<Func<AiFeedbackRating, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<AiFeedbackRating, bool>> predicate,
        CancellationToken ct = default);
}
