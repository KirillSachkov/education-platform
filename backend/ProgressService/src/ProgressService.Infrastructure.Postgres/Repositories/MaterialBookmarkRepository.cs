using System.Linq.Expressions;
using Core.Database;
using Dapper;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.Bookmarks;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class MaterialBookmarkRepository : IMaterialBookmarkRepository
{
    private readonly ProgressDbContext _dbContext;
    private readonly ITransactionManager _transactionManager;

    public MaterialBookmarkRepository(ProgressDbContext dbContext, ITransactionManager transactionManager)
    {
        _dbContext = dbContext;
        _transactionManager = transactionManager;
    }

    public async Task<bool> AddIfMissingAsync(MaterialBookmark bookmark, CancellationToken cancellationToken = default)
    {
        const string sql = """
                           INSERT INTO material_bookmarks (
                               id,
                               user_id,
                               course_id,
                               target_entity_type,
                               target_entity_id,
                               created_at,
                               updated_at
                           )
                           VALUES (
                               @Id,
                               @UserId,
                               @CourseId,
                               @TargetType,
                               @TargetId,
                               @CreatedAt,
                               @UpdatedAt
                           )
                           ON CONFLICT (user_id, course_id, target_entity_type, target_entity_id) DO NOTHING;
                           """;

        int affectedRows = await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    bookmark.Id,
                    bookmark.UserId,
                    bookmark.CourseId,
                    TargetType = bookmark.EntityReference.Type.ToString(),
                    TargetId = bookmark.EntityReference.Id,
                    bookmark.CreatedAt,
                    bookmark.UpdatedAt
                },
                cancellationToken: cancellationToken));

        return affectedRows > 0;
    }

    public void Remove(MaterialBookmark bookmark)
    {
        _dbContext.MaterialBookmarks.Remove(bookmark);
    }

    public Task<MaterialBookmark?> GetByAsync(
        Expression<Func<MaterialBookmark, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.MaterialBookmarks.FirstOrDefaultAsync(predicate, cancellationToken);
    }

    public Task<bool> ExistsAsync(
        Expression<Func<MaterialBookmark, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.MaterialBookmarks.AnyAsync(predicate, cancellationToken);
    }

    public async Task<int> DeleteByCourseIdAsync(Guid courseId, CancellationToken cancellationToken = default)
    {
        const string sql = "DELETE FROM material_bookmarks WHERE course_id = @CourseId";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { CourseId = courseId },
                cancellationToken: cancellationToken));
    }

    public async Task<int> DeleteByTargetEntityIdsAsync(
        IReadOnlyCollection<Guid> targetEntityIds,
        CancellationToken cancellationToken = default)
    {
        if (targetEntityIds.Count == 0)
        {
            return 0;
        }

        const string sql = "DELETE FROM material_bookmarks WHERE target_entity_id = ANY(@TargetIds)";

        return await _transactionManager.GetDbConnection().ExecuteAsync(
            new CommandDefinition(
                sql,
                new { TargetIds = targetEntityIds.ToArray() },
                cancellationToken: cancellationToken));
    }
}
