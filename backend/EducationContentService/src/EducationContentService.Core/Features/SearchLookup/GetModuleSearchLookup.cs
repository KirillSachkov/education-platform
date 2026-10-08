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

public sealed record GetModuleSearchLookupQuery(Guid ModuleId) : IQuery;

public sealed class GetModuleSearchLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/search/modules/{moduleId:guid}", async Task<EndpointResult<ModuleSearchLookupDto>> (
                [FromRoute] Guid moduleId,
                [FromServices] GetModuleSearchLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetModuleSearchLookupQuery(moduleId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetModuleSearchLookupHandler
    : IQueryHandlerWithResult<ModuleSearchLookupDto, GetModuleSearchLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetModuleSearchLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<ModuleSearchLookupDto, Error>> Handle(
        GetModuleSearchLookupQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                m.id,
                m.title,
                m.description,
                m.status,
                m.updated_at,
                c.id AS course_id,
                c.slug AS course_slug,
                c.title AS course_title,
                c.author_id AS author_id,
                COALESCE(
                    ARRAY_AGG(item_access.access_type) FILTER (WHERE item_access.access_type IS NOT NULL),
                    ARRAY[]::text[]
                ) AS item_access_types
            FROM modules m
            LEFT JOIN course_items ci
                ON ci.reference_id = m.id AND ci.item_type = 'Module'
            LEFT JOIN courses c
                ON c.id = ci.course_id
            LEFT JOIN LATERAL (
                SELECT mat.access_type
                FROM module_items mi
                JOIN materials mat
                    ON mi.item_type = 'Material' AND mi.reference_id = mat.id
                AND mat.status = 'PUBLISHED'
                WHERE mi.module_id = m.id
                UNION ALL
                SELECT iss.access_type
                FROM module_items mi
                JOIN issues iss
                    ON mi.item_type = 'Issue' AND mi.reference_id = iss.id
                AND iss.status = 'PUBLISHED'
                WHERE mi.module_id = m.id
            ) item_access ON TRUE
            WHERE m.id = @ModuleId
            GROUP BY
                m.id,
                m.title,
                m.description,
                m.status,
                m.updated_at,
                c.id,
                c.title,
                c.slug,
                c.author_id;
            """;

        ModuleSearchLookupRow? row = await connection.QueryFirstOrDefaultAsync<ModuleSearchLookupRow>(
            new CommandDefinition(sql, new { query.ModuleId }, cancellationToken: cancellationToken));

        if (row is null)
        {
            return GeneralErrors.NotFound(query.ModuleId);
        }

        return new ModuleSearchLookupDto(
            row.Id,
            row.CourseId,
            row.CourseSlug,
            row.Title,
            row.Description,
            row.CourseTitle,
            SearchLookupEnumConverter.ToPublicationStatus(row.Status),
            row.UpdatedAt,
            row.ItemAccessTypes.Length == 0
                ? ContentAccessTagBuilder.BuildCoursePublicDefault()
                : ContentAccessTagBuilder.BuildMany(
                    row.ItemAccessTypes,
                    row.Id,
                    row.CourseId.HasValue ? [row.CourseId.Value] : []),
            row.AuthorId);
    }

    private sealed class ModuleSearchLookupRow
    {
        public Guid Id { get; init; }
        public Guid? CourseId { get; init; }
        public string? CourseSlug { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public string? CourseTitle { get; init; }
        public string Status { get; init; } = null!;
        public DateTime UpdatedAt { get; init; }
        public string[] ItemAccessTypes { get; init; } = [];
        public Guid? AuthorId { get; init; }
    }
}
