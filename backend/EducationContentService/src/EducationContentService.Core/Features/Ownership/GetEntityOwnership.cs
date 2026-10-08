using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Ownership;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Ownership;

public sealed record GetEntityOwnershipQuery(string EntityType, Guid EntityId) : IQuery;

public sealed class GetEntityOwnershipEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/ownership/{entityType}/{entityId:guid}",
                async Task<EndpointResult<EntityOwnershipDto>> (
                    [FromRoute] string entityType,
                    [FromRoute] Guid entityId,
                    [FromServices] GetEntityOwnershipHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetEntityOwnershipQuery(entityType, entityId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetEntityOwnershipHandler : IQueryHandlerWithResult<EntityOwnershipDto, GetEntityOwnershipQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetEntityOwnershipHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<EntityOwnershipDto, Error>> Handle(
        GetEntityOwnershipQuery query, CancellationToken cancellationToken)
    {
        string? sql = query.EntityType switch
        {
            // created_by_user_id релевантен только для material — для course/issue всегда NULL
            // (но колонку селектим явно, чтобы Dapper-маппинг OwnershipRow не падал, #400).
            ResourceTypes.COURSE => """
                SELECT id AS course_id, author_id, NULL::uuid AS created_by_user_id, 1 AS priority
                FROM courses
                WHERE id = @EntityId
                """,
            ResourceTypes.ISSUE => """
                SELECT sub.course_id,
                    COALESCE(c.author_id, i.author_id) AS author_id,
                    i.author_id AS created_by_user_id,
                    sub.priority
                FROM issues i
                LEFT JOIN LATERAL (
                    SELECT ci.course_id, 1 AS priority
                    FROM course_items ci
                    WHERE ci.reference_id = i.project_id AND ci.item_type = 'Project'
                    UNION ALL
                    SELECT ci.course_id, 2 AS priority
                    FROM module_items mi
                    JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                    WHERE mi.item_type = 'Issue' AND mi.reference_id = i.id
                    UNION ALL
                    SELECT NULL::uuid, 3
                ) sub ON TRUE
                LEFT JOIN courses c ON c.id = sub.course_id
                WHERE i.id = @EntityId
                """,
            // created_by_user_id = фактический создатель материала (materials.author_id),
            // независимо от priority-пути (course-bound / orphan). Через correlated subquery,
            // чтобы значение было одинаковым во всех ветках UNION (#400).
            ResourceTypes.MATERIAL => """
                SELECT course_id, author_id,
                    (SELECT author_id FROM materials WHERE id = @EntityId) AS created_by_user_id,
                    priority
                FROM (
                    SELECT cm.course_id, c.author_id, 1 AS priority
                    FROM course_materials cm
                    JOIN courses c ON c.id = cm.course_id
                    WHERE cm.material_id = @EntityId
                    UNION ALL
                    SELECT ci.course_id, c.author_id, 2 AS priority
                    FROM module_items mi
                    JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                    JOIN courses c ON c.id = ci.course_id
                    WHERE mi.item_type = 'Material' AND mi.reference_id = @EntityId
                    UNION ALL
                    SELECT NULL::uuid, m.author_id, 3 AS priority
                    FROM materials m
                    WHERE m.id = @EntityId
                ) sub
                """,
            ResourceTypes.QUIZ => """
                SELECT course_id, author_id,
                    (SELECT author_id FROM quizzes WHERE id = @EntityId) AS created_by_user_id,
                    priority
                FROM (
                    SELECT cq.course_id, c.author_id, 1 AS priority
                    FROM course_quizzes cq
                    JOIN courses c ON c.id = cq.course_id
                    WHERE cq.quiz_id = @EntityId
                    UNION ALL
                    SELECT NULL::uuid, q.author_id, 2 AS priority
                    FROM quizzes q
                    WHERE q.id = @EntityId
                ) sub
                """,
            "project" => """
                SELECT ci.course_id,
                    COALESCE(c.author_id, p.author_id) AS author_id,
                    p.author_id AS created_by_user_id,
                    CASE WHEN ci.course_id IS NULL THEN 2 ELSE 1 END AS priority
                FROM projects p
                LEFT JOIN course_items ci
                    ON ci.reference_id = p.id AND ci.item_type = 'Project'
                LEFT JOIN courses c ON c.id = ci.course_id
                WHERE p.id = @EntityId
                """,
            "module" => """
                SELECT ci.course_id,
                    COALESCE(c.author_id, m.author_id) AS author_id,
                    m.author_id AS created_by_user_id,
                    CASE WHEN ci.course_id IS NULL THEN 2 ELSE 1 END AS priority
                FROM modules m
                LEFT JOIN course_items ci
                    ON ci.reference_id = m.id AND ci.item_type = 'Module'
                LEFT JOIN courses c ON c.id = ci.course_id
                WHERE m.id = @EntityId
                """,
            ResourceTypes.COLLECTION => """
                SELECT col.course_id,
                    COALESCE(c.author_id, col.author_id) AS author_id,
                    col.author_id AS created_by_user_id,
                    CASE WHEN col.course_id IS NULL THEN 2 ELSE 1 END AS priority
                FROM collections col
                LEFT JOIN courses c ON c.id = col.course_id
                WHERE col.id = @EntityId
                """,
            _ => null
        };

        if (sql is null)
            return new EntityOwnershipDto(null, null);

        DbConnection connection = _transactionManager.GetDbConnection();

        List<OwnershipRow> rows = (await connection.QueryAsync<OwnershipRow>(
                new CommandDefinition(sql, new { query.EntityId }, cancellationToken: cancellationToken)))
            .ToList();

        if (rows.Count == 0)
            return new EntityOwnershipDto(null, null);

        OwnershipRow primary = rows
            .OrderBy(row => row.Priority)
            .ThenBy(row => row.CourseId)
            .First();
        IEnumerable<Guid?> managerCandidates = query.EntityType switch
        {
            // #657: material creator and owners of every course containing the material
            // share the edit surface.
            ResourceTypes.MATERIAL or ResourceTypes.QUIZ =>
                rows.SelectMany(row => new[] { row.AuthorId, row.CreatedByUserId }),
            // Project/module/collection mutation policy is direct-author-only even when
            // the entity is attached to a course. Notification ownership is broader.
            "project" or "module" or ResourceTypes.COLLECTION => [primary.CreatedByUserId],
            // The issue edit surface follows the repository's resolved course author,
            // with the direct author used only for orphan issues.
            ResourceTypes.ISSUE => [primary.AuthorId],
            _ => [primary.AuthorId],
        };
        Guid[] managerUserIds = managerCandidates
            .OfType<Guid>()
            .Distinct()
            .Order()
            .ToArray();

        return new EntityOwnershipDto(
            primary.CourseId,
            primary.AuthorId,
            primary.CreatedByUserId,
            managerUserIds);
    }

    private sealed record OwnershipRow(
        Guid? CourseId,
        Guid? AuthorId,
        Guid? CreatedByUserId,
        int Priority);
}
