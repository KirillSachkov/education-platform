using System.Linq.Expressions;
using TrainerService.Core.Database;
using TrainerService.Domain;
using TrainerService.Domain.Questions;

namespace TrainerService.Infrastructure.Postgres.Repositories;

internal sealed class TrainerQuestionsRepository : ITrainerQuestionsRepository
{
    private readonly TrainerServiceDbContext _dbContext;

    public TrainerQuestionsRepository(TrainerServiceDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(TrainerQuestion question, CancellationToken ct = default) =>
        await _dbContext.TrainerQuestions.AddAsync(question, ct);

    public Task RemoveAsync(TrainerQuestion question, CancellationToken ct = default)
    {
        _dbContext.TrainerQuestions.Remove(question);
        return Task.CompletedTask;
    }

    public async Task<Result<TrainerQuestion, Error>> GetByAsync(
        Expression<Func<TrainerQuestion, bool>> predicate,
        CancellationToken ct = default)
    {
        TrainerQuestion? question = await _dbContext.TrainerQuestions
            .Include(q => q.Options)
            .FirstOrDefaultAsync(predicate, ct);
        return question is null
            ? TrainerServiceErrors.Question.NotFound(Guid.Empty)
            : question;
    }

    public async Task<IReadOnlyList<TrainerQuestion>> GetManyByAsync(
        Expression<Func<TrainerQuestion, bool>> predicate,
        CancellationToken ct = default) =>
        await _dbContext.TrainerQuestions
            .Include(q => q.Options)
            .Where(predicate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TrainerQuestion>> GetOpenWithoutReferenceAsync(
        IReadOnlyCollection<Guid>? bankIds,
        int limit,
        CancellationToken ct = default)
    {
        IQueryable<TrainerQuestion> query = _dbContext.TrainerQuestions
            .Where(q => q.Type == TrainerQuestionType.OPEN_TEXT && q.ReferenceAnswer == null);

        if (bankIds is not null)
            query = query.Where(q => bankIds.Contains(q.BankId));

        return await query
            .OrderBy(q => q.Id)
            .Take(limit)
            .ToListAsync(ct);
    }

    public Task<string?> GetMaxSortKeyAsync(Guid bankId, CancellationToken ct = default) =>
        _dbContext.TrainerQuestions
            .Where(q => q.BankId == bankId)
            .OrderByDescending(q => q.SortKey)
            .Select(q => q.SortKey)
            .FirstOrDefaultAsync(ct);

    public Task<int> CountByAsync(
        Expression<Func<TrainerQuestion, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.TrainerQuestions.CountAsync(predicate, ct);

    public Task<bool> ExistsAsync(
        Expression<Func<TrainerQuestion, bool>> predicate,
        CancellationToken ct = default) =>
        _dbContext.TrainerQuestions.AnyAsync(predicate, ct);
}
