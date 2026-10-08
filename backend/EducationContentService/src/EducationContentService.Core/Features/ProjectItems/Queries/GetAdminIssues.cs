using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Issues;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProjectItems.Queries;

/// <summary>
///     Admin listing / search / export of issues across all authors and statuses.
///     One endpoint backs three MCP tools: list (filters), search (<paramref name="Search"/>),
///     and course export (<paramref name="CourseId"/> + <paramref name="IncludeContent"/>).
///     Filters are conjunctive; a null filter is ignored. Course/module placement is resolved
///     server-side via course_items / module_items (never trust client-supplied placement).
/// </summary>
public sealed record GetAdminIssuesQuery(
    Guid? CourseId,
    Guid? ProjectId,
    Guid? ModuleId,
    string? Status,
    string? AccessType,
    string? TitleContains,
    string? Search,
    bool IncludeContent,
    int Limit,
    int Offset) : IQuery;

public sealed class GetAdminIssuesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("issues/admin-list", async Task<EndpointResult<IReadOnlyList<AdminIssueListItemDto>>> (
                [FromQuery] Guid? courseId,
                [FromQuery] Guid? projectId,
                [FromQuery] Guid? moduleId,
                [FromQuery] string? status,
                [FromQuery] string? accessType,
                [FromQuery] string? titleContains,
                [FromQuery] string? search,
                [FromQuery] bool? includeContent,
                [FromQuery] int? limit,
                [FromQuery] int? offset,
                [FromServices] GetAdminIssuesHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetAdminIssuesQuery(
                        courseId,
                        projectId,
                        moduleId,
                        NullIfBlank(status),
                        NullIfBlank(accessType),
                        NullIfBlank(titleContains),
                        NullIfBlank(search),
                        includeContent ?? false,
                        Math.Clamp(limit ?? 200, 1, 500),
                        Math.Max(offset ?? 0, 0)),
                    cancellationToken))
            // Admin-list across ALL authors → admin/service-only (consumer — MCP admin-server).
            // На Issues.MANAGE (есть у platform-author) это был cross-author IDOR — зеркало GetAllCoursesAdmin.
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.SERVICE);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class GetAdminIssuesHandler
    : IQueryHandlerWithResult<IReadOnlyList<AdminIssueListItemDto>, GetAdminIssuesQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetAdminIssuesHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<IReadOnlyList<AdminIssueListItemDto>, Error>> Handle(
        GetAdminIssuesQuery query, CancellationToken cancellationToken = default)
    {
        // course_items / module_items store item_type in PascalCase ('Project', 'Issue') — see GetCourseBuilder.
        // A project can be attached to MULTIPLE courses (see GetIssueCourseBindings), so the course_items
        // LEFT JOIN can fan out N rows per issue. DISTINCT ON (i.id) collapses to ONE row, choosing the PRIMARY
        // course placement = earliest-attached (smallest ci.id, Guid v7 time-ordered) — consistent with the
        // "primary binding" convention in GetIssueCourseBindings. When @CourseId is set, the WHERE already
        // limits to that course, so the chosen placement is that course. An issue is attachable to at most one
        // module (domain rule), so the module_items LEFT JOIN is 0..1 and does not fan out.
        const string sql = """
                           SELECT
                               "IssueId", "ProjectId", "ProjectTitle", "Title", "Status", "AccessType",
                               "CreatedAt", "UpdatedAt", "ProjectSortKey", "CourseId", "CourseTitle",
                               "CourseProjectSortKey", "ModuleId", "ModuleTitle", "ModuleSortKey",
                               "InternalMaterialsCount", "Content"
                           FROM (
                               SELECT DISTINCT ON (i.id)
                                   i.id            AS "IssueId",
                                   i.project_id    AS "ProjectId",
                                   p.title         AS "ProjectTitle",
                                   i.title         AS "Title",
                                   i.status        AS "Status",
                                   i.access_type   AS "AccessType",
                                   i.created_at    AS "CreatedAt",
                                   i.updated_at    AS "UpdatedAt",
                                   pi.sort_key     AS "ProjectSortKey",
                                   ci.course_id    AS "CourseId",
                                   c.title         AS "CourseTitle",
                                   ci.sort_key     AS "CourseProjectSortKey",
                                   mi.module_id    AS "ModuleId",
                                   m.title         AS "ModuleTitle",
                                   mi.sort_key     AS "ModuleSortKey",
                                   jsonb_array_length(COALESCE(i.internal_materials, '[]'::jsonb)) AS "InternalMaterialsCount",
                                   CASE WHEN @IncludeContent THEN i.content ELSE NULL END AS "Content"
                               FROM issues i
                               JOIN projects p ON p.id = i.project_id
                               LEFT JOIN project_items pi ON pi.issue_id = i.id
                               LEFT JOIN course_items ci ON ci.reference_id = i.project_id AND ci.item_type = 'Project'
                               LEFT JOIN courses c ON c.id = ci.course_id
                               LEFT JOIN module_items mi ON mi.reference_id = i.id AND mi.item_type = 'Issue'
                               LEFT JOIN modules m ON m.id = mi.module_id
                               WHERE (@ProjectId IS NULL OR i.project_id = @ProjectId)
                                 AND (@CourseId IS NULL OR ci.course_id = @CourseId)
                                 AND (@ModuleId IS NULL OR mi.module_id = @ModuleId)
                                 AND (@Status IS NULL OR i.status = @Status)
                                 AND (@AccessType IS NULL OR i.access_type = @AccessType)
                                 AND (@TitleContains IS NULL OR i.title ILIKE '%' || @TitleContains || '%')
                                 AND (@Search IS NULL
                                      OR i.title ILIKE '%' || @Search || '%'
                                      OR COALESCE(i.content, '') ILIKE '%' || @Search || '%')
                               ORDER BY i.id, ci.id NULLS LAST
                           ) t
                           ORDER BY "CourseId" NULLS LAST, "CourseProjectSortKey" NULLS LAST,
                                    "ProjectSortKey" NULLS LAST, "CreatedAt" ASC, "IssueId" ASC
                           OFFSET @Offset
                           LIMIT @Limit;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IReadOnlyList<AdminIssueListItemDto> rows = (await connection.QueryAsync<AdminIssueListItemDto>(
            sql,
            new
            {
                query.CourseId,
                query.ProjectId,
                query.ModuleId,
                query.Status,
                query.AccessType,
                query.TitleContains,
                query.Search,
                query.IncludeContent,
                query.Limit,
                query.Offset,
            })).ToList();

        return Result.Success<IReadOnlyList<AdminIssueListItemDto>, Error>(rows);
    }
}
