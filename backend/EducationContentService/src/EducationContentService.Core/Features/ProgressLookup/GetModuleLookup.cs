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

public sealed record GetModuleLookupQuery(Guid CourseId, Guid ModuleId) : IQuery;

public sealed class GetModuleLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/progress/courses/{courseId:guid}/modules/{moduleId:guid}",
            async Task<EndpointResult<ModuleDto>> (
                [FromRoute] Guid courseId,
                [FromRoute] Guid moduleId,
                [FromServices] GetModuleLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetModuleLookupQuery(courseId, moduleId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetModuleLookupHandler : IQueryHandlerWithResult<ModuleDto, GetModuleLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetModuleLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<ModuleDto, Error>> Handle(
        GetModuleLookupQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                (SELECT COUNT(*) FROM module_items mi WHERE mi.module_id = m.id) AS module_items_total
            FROM modules m
            INNER JOIN course_items ci
                ON ci.reference_id = m.id AND ci.item_type = 'Module'
            WHERE m.id = @ModuleId
              AND ci.course_id = @CourseId;
            """;

        int? moduleItemsTotal = await connection.QueryFirstOrDefaultAsync<int?>(sql, new
        {
            query.ModuleId,
            query.CourseId
        });

        if (moduleItemsTotal is null)
            return GeneralErrors.NotFound(query.ModuleId);

        return new ModuleDto(moduleItemsTotal.Value);
    }
}
