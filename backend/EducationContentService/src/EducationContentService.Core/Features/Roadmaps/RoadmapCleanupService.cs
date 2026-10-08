using System.Data.Common;
using Core.Database;
using Dapper;

namespace EducationContentService.Core.Features.Roadmaps;

public sealed class RoadmapCleanupService
{
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<RoadmapCleanupService> _logger;

    public RoadmapCleanupService(
        ITransactionManager transactionManager,
        ILogger<RoadmapCleanupService> logger)
    {
        _transactionManager = transactionManager;
        _logger = logger;
    }

    /// <summary>
    /// Remove all roadmap nodes referencing a deleted entity.
    /// </summary>
    public async Task RemoveNodesReferencingEntityAsync(
        Guid entityId, string entityType, CancellationToken ct = default)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           DELETE FROM roadmap_nodes
                           WHERE node_type = 'EntityReference'
                             AND data::jsonb->>'entityId' = @EntityId
                             AND data::jsonb->>'entityType' = @EntityType
                           """;

        int deleted = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { EntityId = entityId.ToString(), EntityType = entityType }, cancellationToken: ct));

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Removed {Count} roadmap nodes referencing deleted {EntityType} {EntityId}",
                deleted, entityType, entityId);
        }
    }

    /// <summary>
    /// Delete the roadmap bound to a course when the course is hard-deleted.
    /// Cascade delete will remove nodes and edges automatically.
    /// </summary>
    public async Task DeleteCourseRoadmapAsync(Guid courseId, CancellationToken ct = default)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = "DELETE FROM roadmaps WHERE course_id = @CourseId";

        int deleted = await connection.ExecuteAsync(
            new CommandDefinition(sql, new { CourseId = courseId }, cancellationToken: ct));

        if (deleted > 0)
        {
            _logger.LogInformation("Deleted roadmap for course {CourseId}", courseId);
        }
    }
}
