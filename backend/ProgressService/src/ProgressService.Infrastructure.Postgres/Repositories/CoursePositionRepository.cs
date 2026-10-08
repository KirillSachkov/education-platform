using System.Linq.Expressions;
using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.CoursePositions;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class CoursePositionRepository : ICoursePositionRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public CoursePositionRepository(
        ProgressDbContext dbContext,
        ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task UpsertAsync(
        Guid userId,
        Guid courseId,
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
                           INSERT INTO course_positions (
                               id,
                               user_id,
                               course_id,
                               entity_type,
                               entity_id,
                               opened_at,
                               created_at
                           )
                           VALUES (
                               @Id,
                               @UserId,
                               @CourseId,
                               @EntityType,
                               @EntityId,
                               @OpenedAt,
                               @CreatedAt
                           )
                           ON CONFLICT (user_id, course_id) DO UPDATE SET
                               entity_type = EXCLUDED.entity_type,
                               entity_id = EXCLUDED.entity_id,
                               opened_at = EXCLUDED.opened_at;
                           """;

        DateTime now = DateTime.UtcNow;

        await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    Id = Guid.CreateVersion7(),
                    UserId = userId,
                    CourseId = courseId,
                    EntityType = entityType,
                    EntityId = entityId,
                    OpenedAt = now,
                    CreatedAt = now,
                },
                cancellationToken: cancellationToken));
    }

    public async Task<CoursePosition?> GetByAsync(
        Expression<Func<CoursePosition, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.CoursePositions
            .AsNoTracking()
            .FirstOrDefaultAsync(predicate, cancellationToken);
    }
}
