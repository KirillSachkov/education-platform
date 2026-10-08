using System.Linq.Expressions;
using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Enrollments;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class CourseEnrollmentRepository : ICourseEnrollmentRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public CourseEnrollmentRepository(ProgressDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task AddAsync(
        CourseEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.CourseEnrollments.AddAsync(enrollment, cancellationToken);
    }

    public async Task<Result<CourseEnrollment, Error>> GetByAsync(
        Expression<Func<CourseEnrollment, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        CourseEnrollment? enrollment = await _dbContext.CourseEnrollments
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return enrollment is null
            ? ProgressErrors.EnrollmentNotFound()
            : enrollment;
    }

    public async Task<List<CourseEnrollment>> GetManyByAsync(
        Expression<Func<CourseEnrollment, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.CourseEnrollments
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<CourseEnrollment, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.CourseEnrollments.AnyAsync(predicate, cancellationToken);
    }

    public async Task<int> DeleteByCourseIdAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        // Raw SQL to leverage PostgreSQL FK cascades on material_progress, module_progress, etc.
        const string sql = "DELETE FROM course_enrollments WHERE course_id = @CourseId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { CourseId = courseId },
                cancellationToken: cancellationToken));
    }
}
