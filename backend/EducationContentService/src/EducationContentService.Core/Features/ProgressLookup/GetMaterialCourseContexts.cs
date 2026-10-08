using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.ProgressLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ProgressLookup;

/// <summary>
///     Возвращает все пары (CourseId, ModuleId), в которых присутствует материал через
///     <c>module_items</c>. ProgressService использует это, чтобы закрыть <c>module_item_progress</c>
///     во всех enrollment'ах пользователя, где содержится данный материал.
/// </summary>
public sealed record GetMaterialCourseContextsQuery(Guid MaterialId) : IQuery;

public sealed class GetMaterialCourseContextsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/progress/materials/{materialId:guid}/course-contexts",
                async Task<EndpointResult<IReadOnlyList<MaterialCourseContextDto>>> (
                        [FromRoute] Guid materialId,
                        [FromServices] GetMaterialCourseContextsHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMaterialCourseContextsQuery(materialId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetMaterialCourseContextsHandler
    : IQueryHandlerWithResult<IReadOnlyList<MaterialCourseContextDto>, GetMaterialCourseContextsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetMaterialCourseContextsHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<IReadOnlyList<MaterialCourseContextDto>, Error>> Handle(
        GetMaterialCourseContextsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // Материал может быть в нескольких курсах. Берём только те вхождения, что
        // находятся в module_items (актуальная структура модуля), — именно для них
        // существует module_item_progress. Запись в course_materials без module_items
        // означает «материал прикреплён к курсу, но не в модуле» — такого blueprint
        // item'а нет, cascade не нужен.
        const string sql = """
                           SELECT
                               ci.course_id AS CourseId,
                               mi.module_id AS ModuleId,
                               (SELECT COUNT(*)::integer FROM module_items mi2 WHERE mi2.module_id = mi.module_id)
                                   AS ModuleItemsTotal
                           FROM module_items mi
                           INNER JOIN course_items ci
                               ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                           WHERE mi.reference_id = @MaterialId
                             AND mi.item_type = 'Material';
                           """;

        IEnumerable<MaterialCourseContextDto> rows = await connection.QueryAsync<MaterialCourseContextDto>(
            new CommandDefinition(
                sql,
                new { query.MaterialId },
                cancellationToken: cancellationToken));

        IReadOnlyList<MaterialCourseContextDto> result = rows.ToList();
        return Result.Success<IReadOnlyList<MaterialCourseContextDto>, Error>(result);
    }
}
