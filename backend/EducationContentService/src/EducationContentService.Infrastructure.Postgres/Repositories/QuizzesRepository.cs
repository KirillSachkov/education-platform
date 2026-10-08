using System.Linq.Expressions;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class QuizzesRepository : IQuizzesRepository
{
    private readonly EducationDbContext _dbContext;

    public QuizzesRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Quiz quiz, CancellationToken cancellationToken = default)
    {
        await _dbContext.Quizzes.AddAsync(quiz, cancellationToken);
    }

    public void Delete(Quiz quiz) => _dbContext.Quizzes.Remove(quiz);

    public async Task<Result<Quiz, Error>> GetByAsync(
        Expression<Func<Quiz, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Quiz? quiz = await _dbContext.Quizzes.FirstOrDefaultAsync(predicate, cancellationToken);

        return quiz is null
            ? GeneralErrors.NotFound()
            : quiz;
    }

    public async Task<IReadOnlyList<Quiz>> GetManyByAsync(
        Expression<Func<Quiz, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await _dbContext.Quizzes.Where(predicate).ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(
        Expression<Func<Quiz, bool>> predicate,
        CancellationToken cancellationToken = default)
        => _dbContext.Quizzes.AnyAsync(predicate, cancellationToken);

    public Task<bool> ExistsByTitleAsync(Title title, Guid? excludeId, CancellationToken cancellationToken = default)
        => _dbContext.Quizzes
            .AnyAsync(
                q => q.Title == title
                     && (q.Status == PublicationStatus.PUBLISHED || q.Status == PublicationStatus.ARCHIVED)
                     && (excludeId == null || q.Id != excludeId),
                cancellationToken);
}
