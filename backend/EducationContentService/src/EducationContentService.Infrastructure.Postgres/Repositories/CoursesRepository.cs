using System.Data;
using System.Data.Common;
using System.Linq.Expressions;
using Dapper;
using EducationContentService.Core.Features.Courses;
using Microsoft.EntityFrameworkCore.Storage;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class CoursesRepository : ICoursesRepository
{
    private readonly EducationDbContext _dbContext;

    public CoursesRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Course course, CancellationToken cancellationToken = default)
    {
        await _dbContext.Courses.AddAsync(course, cancellationToken);
    }

    public async Task<Result<Course, Error>> GetByAsync(
        Expression<Func<Course, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Course? course = await _dbContext.Courses.FirstOrDefaultAsync(predicate, cancellationToken);

        return course is null
            ? GeneralErrors.NotFound()
            : course;
    }

    public async Task<Result<Course, Error>> GetByAsync(
        Expression<Func<Course, bool>> predicate,
        Expression<Func<Course, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<Course> query = _dbContext.Courses.Where(predicate);

        if (orderBy != null)
        {
            query = descending
                ? query.OrderByDescending(orderBy)
                : query.OrderBy(orderBy);
        }

        Course? course = await query.FirstOrDefaultAsync(cancellationToken);

        return course is null
            ? GeneralErrors.NotFound()
            : course;
    }

    public async Task<List<Course>> GetManyByAsync(
        Expression<Func<Course, bool>> predicate,
        CancellationToken cancellationToken = default)
        => await _dbContext.Courses.Where(predicate).ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(
        Expression<Func<Course, bool>> predicate,
        CancellationToken cancellationToken = default)
        => _dbContext.Courses.AnyAsync(predicate, cancellationToken);

    public Task DeleteAsync(Course course, CancellationToken ct = default)
    {
        _dbContext.Courses.Remove(course);
        return Task.CompletedTask;
    }

    public async Task<long> GetNextAssetOwnershipRevisionAsync(CancellationToken ct = default) =>
        await _dbContext.Database
            .SqlQueryRaw<long>(
                "SELECT nextval('education.asset_ownership_revision_seq') AS \"Value\"")
            .SingleAsync(ct);

    public void Delete(Course item) => _dbContext.Courses.Remove(item);

    public Task<bool> ExistsByTitleAsync(Title title, Guid excludeId, CancellationToken ct = default)
        => _dbContext.Courses
            .AnyAsync(c => c.Title == title && c.Status != PublicationStatus.DRAFT && c.Id != excludeId, ct);

    public async Task<CourseAuthorReassignmentResult> ReassignChildContentAuthorAsync(
        Guid courseId,
        Guid newAuthorId,
        CancellationToken ct = default)
    {
        // Runs on the caller's ambient transaction so the child flips commit atomically with
        // the course-root flip. Bulk SQL mirrors the cascade-delete pattern (DeleteByCourseId).
        DbConnection connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
        DbTransaction? transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var args = new { CourseId = courseId, NewAuthorId = newAuthorId };

        List<Guid> sharedMaterialIds =
            (await connection.QueryAsync<Guid>(new CommandDefinition(
                SHARED_MATERIALS_SQL, args, transaction, cancellationToken: ct))).ToList();

        List<Guid> sharedQuizIds =
            (await connection.QueryAsync<Guid>(new CommandDefinition(
                SHARED_QUIZZES_SQL, args, transaction, cancellationToken: ct))).ToList();

        List<Guid> transferredProjectIds =
            (await connection.QueryAsync<Guid>(new CommandDefinition(
                EXCLUSIVE_PROJECTS_SQL, args, transaction, cancellationToken: ct))).ToList();

        List<Guid> transferredIssueIds =
            (await connection.QueryAsync<Guid>(new CommandDefinition(
                EXCLUSIVE_ISSUES_SQL, args, transaction, cancellationToken: ct))).ToList();

        List<Guid> transferredMaterialIds =
            (await connection.QueryAsync<Guid>(new CommandDefinition(
                EXCLUSIVE_MATERIALS_SQL, args, transaction, cancellationToken: ct))).ToList();

        List<Guid> transferredCollectionIds =
            (await connection.QueryAsync<Guid>(new CommandDefinition(
                COURSE_COLLECTIONS_SQL, args, transaction, cancellationToken: ct))).ToList();

        foreach (string statement in ReassignStatements)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                statement, args, transaction, cancellationToken: ct));
        }

        return new CourseAuthorReassignmentResult(
            sharedMaterialIds,
            sharedQuizIds,
            transferredProjectIds,
            transferredIssueIds,
            transferredMaterialIds,
            transferredCollectionIds);
    }

    private const string SHARED_MATERIALS_SQL = """
        SELECT cm.material_id
        FROM course_materials cm
        WHERE cm.course_id = @CourseId
          AND EXISTS (SELECT 1 FROM course_materials other
                      WHERE other.material_id = cm.material_id AND other.course_id <> @CourseId)
        """;

    private const string SHARED_QUIZZES_SQL = """
        SELECT cq.quiz_id
        FROM course_quizzes cq
        WHERE cq.course_id = @CourseId
          AND EXISTS (SELECT 1 FROM course_quizzes other
                      WHERE other.quiz_id = cq.quiz_id AND other.course_id <> @CourseId)
        """;

    private const string EXCLUSIVE_PROJECTS_SQL = """
        SELECT ci.reference_id
        FROM course_items ci
        WHERE ci.course_id = @CourseId AND ci.item_type = 'Project'
          AND NOT EXISTS (SELECT 1 FROM course_items other
                          WHERE other.reference_id = ci.reference_id AND other.item_type = 'Project'
                            AND other.course_id <> @CourseId)
        """;

    private const string EXCLUSIVE_ISSUES_SQL = """
        SELECT i.id
        FROM issues i
        WHERE i.project_id IN (
            SELECT project_id FROM (
                SELECT ci.reference_id AS project_id
                FROM course_items ci
                WHERE ci.course_id = @CourseId AND ci.item_type = 'Project'
                  AND NOT EXISTS (SELECT 1 FROM course_items other
                                  WHERE other.reference_id = ci.reference_id AND other.item_type = 'Project'
                                    AND other.course_id <> @CourseId)
            ) projects)
        """;

    private const string EXCLUSIVE_MATERIALS_SQL = """
        SELECT cm.material_id
        FROM course_materials cm
        WHERE cm.course_id = @CourseId
          AND NOT EXISTS (SELECT 1 FROM course_materials other
                          WHERE other.material_id = cm.material_id AND other.course_id <> @CourseId)
        """;

    private const string COURSE_COLLECTIONS_SQL = """
        SELECT id FROM collections WHERE course_id = @CourseId
        """;

    // Every child type is flipped only when owned EXCLUSIVELY by this course. Modules/projects
    // are course-scoped by domain convention, but NO db constraint enforces it — so we filter the
    // same way as materials/quizzes (NOT EXISTS in another course): a module shared by a data
    // anomaly is skipped, never yanked from the other course. Issues follow their (exclusive)
    // project. Collections carry a single course_id FK (exclusive by construction).
    private static readonly string[] ReassignStatements =
    [
        """
        UPDATE modules SET author_id = @NewAuthorId
        WHERE id IN (
            SELECT ci.reference_id FROM course_items ci
            WHERE ci.course_id = @CourseId AND ci.item_type = 'Module'
              AND NOT EXISTS (SELECT 1 FROM course_items other
                              WHERE other.reference_id = ci.reference_id AND other.item_type = 'Module'
                                AND other.course_id <> @CourseId))
        """,
        """
        UPDATE projects SET author_id = @NewAuthorId
        WHERE id IN (
            SELECT ci.reference_id FROM course_items ci
            WHERE ci.course_id = @CourseId AND ci.item_type = 'Project'
              AND NOT EXISTS (SELECT 1 FROM course_items other
                              WHERE other.reference_id = ci.reference_id AND other.item_type = 'Project'
                                AND other.course_id <> @CourseId))
        """,
        """
        UPDATE issues SET author_id = @NewAuthorId
        WHERE project_id IN (
            SELECT ci.reference_id FROM course_items ci
            WHERE ci.course_id = @CourseId AND ci.item_type = 'Project'
              AND NOT EXISTS (SELECT 1 FROM course_items other
                              WHERE other.reference_id = ci.reference_id AND other.item_type = 'Project'
                                AND other.course_id <> @CourseId))
        """,
        """
        UPDATE materials SET author_id = @NewAuthorId
        WHERE id IN (
            SELECT cm.material_id FROM course_materials cm
            WHERE cm.course_id = @CourseId
              AND NOT EXISTS (SELECT 1 FROM course_materials other
                              WHERE other.material_id = cm.material_id AND other.course_id <> @CourseId))
        """,
        """
        UPDATE quizzes SET author_id = @NewAuthorId
        WHERE id IN (
            SELECT cq.quiz_id FROM course_quizzes cq
            WHERE cq.course_id = @CourseId
              AND NOT EXISTS (SELECT 1 FROM course_quizzes other
                              WHERE other.quiz_id = cq.quiz_id AND other.course_id <> @CourseId))
        """,
        "UPDATE collections SET author_id = @NewAuthorId WHERE course_id = @CourseId",
    ];
}
