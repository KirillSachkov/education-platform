using System.Linq.Expressions;
using TrainerService.Domain.Questions;

namespace TrainerService.Core.Database;

public interface ITrainerQuestionsRepository
{
    Task AddAsync(TrainerQuestion question, CancellationToken ct = default);

    Task RemoveAsync(TrainerQuestion question, CancellationToken ct = default);

    Task<Result<TrainerQuestion, Error>> GetByAsync(
        Expression<Func<TrainerQuestion, bool>> predicate,
        CancellationToken ct = default);

    Task<IReadOnlyList<TrainerQuestion>> GetManyByAsync(
        Expression<Func<TrainerQuestion, bool>> predicate,
        CancellationToken ct = default);

    /// <summary>
    ///     SQL-bounded OPEN_TEXT backlog for the reference-answer backfill. Options are intentionally
    ///     not included because open questions cannot have them and the handler only updates the question.
    /// </summary>
    Task<IReadOnlyList<TrainerQuestion>> GetOpenWithoutReferenceAsync(
        IReadOnlyCollection<Guid>? bankIds,
        int limit,
        CancellationToken ct = default);

    Task<string?> GetMaxSortKeyAsync(Guid bankId, CancellationToken ct = default);

    Task<int> CountByAsync(
        Expression<Func<TrainerQuestion, bool>> predicate,
        CancellationToken ct = default);

    Task<bool> ExistsAsync(
        Expression<Func<TrainerQuestion, bool>> predicate,
        CancellationToken ct = default);
}
