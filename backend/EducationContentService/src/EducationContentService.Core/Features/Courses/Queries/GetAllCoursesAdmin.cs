using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Courses;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Courses.Queries;

/// <summary>
///     Admin listing of ALL courses (DRAFT / PUBLISHED / ARCHIVED) across all authors.
///     Used by admin tools (MCP) for course management and audit.
/// </summary>
public sealed record GetAllCoursesAdminQuery(string? Status, int Limit, int Offset) : IQuery;

public sealed class GetAllCoursesAdminEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/admin-list", async Task<EndpointResult<IReadOnlyList<CourseSummaryDto>>> (
                [FromQuery] string? status,
                [FromQuery] int? limit,
                [FromQuery] int? offset,
                [FromServices] GetAllCoursesAdminHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetAllCoursesAdminQuery(status, Math.Clamp(limit ?? 200, 1, 500), Math.Max(offset ?? 0, 0)),
                    cancellationToken))
            // Admin-list возвращает курсы ВСЕХ авторов (DRAFT/ARCHIVED включительно) → строго
            // admin/service-only (потребитель — MCP admin-server по service-токену). На
            // Courses.MANAGE (есть у platform-author) это был cross-author IDOR.
            .RequireAnyRole(PlatformRoles.ADMIN, PlatformRoles.SERVICE);
    }
}

public sealed class GetAllCoursesAdminHandler
    : IQueryHandlerWithResult<IReadOnlyList<CourseSummaryDto>, GetAllCoursesAdminQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetAllCoursesAdminHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<IReadOnlyList<CourseSummaryDto>, Error>> Handle(
        GetAllCoursesAdminQuery query, CancellationToken cancellationToken = default)
    {
        const string sql = """
                           SELECT
                               c.id           AS "Id",
                               c.author_id    AS "AuthorId",
                               c.slug         AS "Slug",
                               c.title        AS "Title",
                               c.description  AS "Description",
                               c.status       AS "Status",
                               c.kind         AS "Kind",
                               c.image_id     AS "ImageId",
                               c.video_id     AS "VideoId",
                               c.is_new       AS "IsNew",
                               c.sort_key     AS "SortKey",
                               c.created_at   AS "CreatedAt",
                               c.updated_at   AS "UpdatedAt",
                               c.show_in_full_access AS "ShowInFullAccess",
                               c.is_catalog_listed   AS "IsCatalogListed",
                               -- #637: CourseSummaryDto несёт author credit (имя/аватар), но это
                               -- MCP/audit-листинг, а не UI-surface — author enrichment здесь не нужен.
                               -- NULL обязателен: Dapper матчит ВСЕ позиционные параметры ctor'а с
                               -- колонками, иначе constructor-bind падает → 500.
                               NULL::text AS "AuthorDisplayName",
                               NULL::text AS "AuthorAvatarUrl"
                           FROM courses c
                           WHERE (@Status IS NULL OR c.status = @Status)
                           ORDER BY c.author_id ASC, c.sort_key ASC, c.id ASC
                           OFFSET @Offset
                           LIMIT @Limit;
                           """;

        DbConnection connection = _transactionManager.GetDbConnection();

        IReadOnlyList<CourseSummaryDto> rows = (await connection.QueryAsync<CourseSummaryDto>(
            sql,
            new
            {
                query.Status,
                query.Limit,
                query.Offset,
            })).ToList();

        return Result.Success<IReadOnlyList<CourseSummaryDto>, Error>(rows);
    }
}
