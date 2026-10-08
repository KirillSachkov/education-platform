using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.SearchLookup;
using EducationContentService.Core.Features.ContentAccess;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.SearchLookup;

public sealed record GetIssueSearchLookupQuery(Guid IssueId) : IQuery;

public sealed class GetIssueSearchLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/search/issues/{issueId:guid}", async Task<EndpointResult<IssueSearchLookupDto>> (
                [FromRoute] Guid issueId,
                [FromServices] GetIssueSearchLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetIssueSearchLookupQuery(issueId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetIssueSearchLookupHandler
    : IQueryHandlerWithResult<IssueSearchLookupDto, GetIssueSearchLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetIssueSearchLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<IssueSearchLookupDto, Error>> Handle(
        GetIssueSearchLookupQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                i.id,
                i.author_id,
                i.project_id,
                i.title,
                i.status,
                i.access_type,
                i.updated_at,
                p.title AS project_title,
                module_parent.module_id,
                module_parent.module_title,
                COALESCE(project_parent.course_id, module_parent.course_id) AS course_id,
                COALESCE(project_parent.course_slug, module_parent.course_slug) AS course_slug,
                COALESCE(project_parent.course_title, module_parent.course_title) AS course_title
            FROM issues i
            LEFT JOIN projects p
                ON p.id = i.project_id
            LEFT JOIN LATERAL (
                SELECT
                    ci.course_id,
                    c.slug AS course_slug,
                    c.title AS course_title
                FROM course_items ci
                JOIN courses c
                    ON c.id = ci.course_id
                WHERE ci.reference_id = i.project_id
                  AND ci.item_type = 'Project'
                LIMIT 1
            ) project_parent ON TRUE
            LEFT JOIN LATERAL (
                SELECT
                    mi.module_id,
                    mo.title AS module_title,
                    ci.course_id,
                    c.slug AS course_slug,
                    c.title AS course_title
                FROM module_items mi
                JOIN modules mo
                    ON mo.id = mi.module_id
                LEFT JOIN course_items ci
                    ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                LEFT JOIN courses c
                    ON c.id = ci.course_id
                WHERE mi.reference_id = i.id
                  AND mi.item_type = 'Issue'
                LIMIT 1
            ) module_parent ON TRUE
            WHERE i.id = @IssueId;
            """;

        IssueSearchLookupRow? row = await connection.QueryFirstOrDefaultAsync<IssueSearchLookupRow>(
            new CommandDefinition(sql, new { query.IssueId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return GeneralErrors.NotFound(query.IssueId);
        }

        return new IssueSearchLookupDto(
            row.Id,
            row.ProjectId,
            row.CourseId,
            row.CourseSlug,
            row.Title,
            row.ModuleId,
            row.CourseTitle,
            row.ProjectTitle,
            row.ModuleTitle,
            SearchLookupEnumConverter.ToPublicationStatus(row.Status),
            ContentAccessTagBuilder.Build(
                row.AccessType,
                row.Id,
                row.CourseId.HasValue ? [row.CourseId.Value] : []),
            row.UpdatedAt,
            row.AuthorId);
    }

    private sealed class IssueSearchLookupRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public Guid ProjectId { get; init; }
        public Guid? CourseId { get; init; }
        public string? CourseSlug { get; init; }
        public string Title { get; init; } = null!;
        public Guid? ModuleId { get; init; }
        public string? CourseTitle { get; init; }
        public string? ProjectTitle { get; init; }
        public string? ModuleTitle { get; init; }
        public string Status { get; init; } = null!;
        public string AccessType { get; init; } = null!;
        public DateTime UpdatedAt { get; init; }
    }
}
