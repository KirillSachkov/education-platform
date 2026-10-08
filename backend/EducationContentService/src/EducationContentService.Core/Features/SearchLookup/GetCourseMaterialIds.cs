using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.SearchLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.SearchLookup;

public sealed record GetCourseMaterialIdsQuery(Guid CourseId) : IQuery;

public sealed class GetCourseMaterialIdsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/search/courses/{courseId:guid}/material-ids",
                async Task<EndpointResult<CourseMaterialIdsDto>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCourseMaterialIdsHandler handler,
                    CancellationToken cancellationToken) =>
                    await handler.Handle(new GetCourseMaterialIdsQuery(courseId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

/// <summary>
///     Возвращает id всех материалов, привязанных к курсу (через course_materials ∪
///     module_items). Используется SearchService для каскадного пере-индекса видимости
///     дочерних материалов при archive/restore курса (#378) — материал может состоять
///     в нескольких курсах, поэтому отдаём полный список членства, не «первичный» курс.
/// </summary>
public sealed class GetCourseMaterialIdsHandler
    : IQueryHandlerWithResult<CourseMaterialIdsDto, GetCourseMaterialIdsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetCourseMaterialIdsHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<CourseMaterialIdsDto, Error>> Handle(
        GetCourseMaterialIdsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // UNION (а не UNION ALL): дедуп. Материал, привязанный и напрямую (course_materials),
        // и через модуль (module_items), не должен пере-индексироваться дважды в каскаде #378.
        const string sql = """
            SELECT cm.material_id
            FROM course_materials cm
            WHERE cm.course_id = @CourseId

            UNION

            SELECT mi.reference_id AS material_id
            FROM module_items mi
            JOIN course_items ci
                ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
            WHERE ci.course_id = @CourseId
              AND mi.item_type = 'Material';
            """;

        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, new { query.CourseId }, cancellationToken: cancellationToken));

        return new CourseMaterialIdsDto(ids.ToArray());
    }
}
