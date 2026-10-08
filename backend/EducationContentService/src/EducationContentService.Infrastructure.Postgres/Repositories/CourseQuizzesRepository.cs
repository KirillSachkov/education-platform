using System.Linq.Expressions;
using EducationContentService.Core.Features.CourseQuizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using Ordering;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class CourseQuizzesRepository : ICourseQuizzesRepository
{
    private readonly EducationDbContext _dbContext;

    public CourseQuizzesRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CourseQuiz item, CancellationToken cancellationToken = default)
    {
        await _dbContext.CourseQuizzes.AddAsync(item, cancellationToken);
    }

    public async Task<bool> AddIfMissingAsync(
        Guid courseId,
        Guid quizId,
        CancellationToken cancellationToken = default)
    {
        bool exists = await _dbContext.CourseQuizzes
            .AnyAsync(cq => cq.CourseId == courseId && cq.QuizId == quizId, cancellationToken);
        if (exists)
            return false;

        CourseQuiz? last = await _dbContext.CourseQuizzes
            .Where(cq => cq.CourseId == courseId)
            .OrderByDescending(cq => cq.SortKey)
            .FirstOrDefaultAsync(cancellationToken);

        SortKey sortKey = last is null
            ? SortKey.Initial()
            : SortKey.After(SortKey.Create(last.SortKey.Value).Value);

        await _dbContext.CourseQuizzes.AddAsync(new CourseQuiz(courseId, quizId, sortKey), cancellationToken);
        return true;
    }

    public async Task<Result<CourseQuiz, Error>> GetByAsync(
        Expression<Func<CourseQuiz, bool>> predicate,
        Expression<Func<CourseQuiz, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<CourseQuiz> query = _dbContext.CourseQuizzes.Where(predicate);

        if (orderBy != null)
        {
            query = descending
                ? query.OrderByDescending(orderBy)
                : query.OrderBy(orderBy);
        }

        CourseQuiz? item = await query.FirstOrDefaultAsync(cancellationToken);

        return item is null
            ? EducationErrors.ItemNotFound("Course", Guid.Empty)
            : item;
    }

    public async Task<List<CourseQuiz>> GetManyByAsync(
        Expression<Func<CourseQuiz, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        await _dbContext.CourseQuizzes
            .Where(predicate)
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(
        Expression<Func<CourseQuiz, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        _dbContext.CourseQuizzes.AnyAsync(predicate, cancellationToken);

    public async Task<List<Guid>> GetCourseIdsAsync(Guid quizId, CancellationToken cancellationToken = default) =>
        await _dbContext.CourseQuizzes
            .Where(cq => cq.QuizId == quizId)
            .Select(cq => cq.CourseId)
            .ToListAsync(cancellationToken);

    public Task DeleteByCourseIdAsync(Guid courseId, CancellationToken cancellationToken = default) =>
        _dbContext.CourseQuizzes
            .Where(cq => cq.CourseId == courseId)
            .ExecuteDeleteAsync(cancellationToken);

    public Task DeleteByQuizIdAsync(Guid quizId, CancellationToken cancellationToken = default) =>
        _dbContext.CourseQuizzes
            .Where(cq => cq.QuizId == quizId)
            .ExecuteDeleteAsync(cancellationToken);

    public void Delete(CourseQuiz item) => _dbContext.CourseQuizzes.Remove(item);
}
