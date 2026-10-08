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

namespace EducationContentService.Core.Features.Materials.Queries;

public sealed record GetMaterialLookupQuery(Guid ModuleId, Guid MaterialId) : IQuery;

public sealed class GetMaterialLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/progress/modules/{moduleId:guid}/materials/{materialId:guid}",
            async Task<EndpointResult<MaterialDto>> (
                [FromRoute] Guid moduleId,
                [FromRoute] Guid materialId,
                [FromServices] GetMaterialLookupHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(new GetMaterialLookupQuery(moduleId, materialId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetMaterialLookupHandler : IQueryHandlerWithResult<MaterialDto, GetMaterialLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetMaterialLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<MaterialDto, Error>> Handle(
        GetMaterialLookupQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                mi.module_id,
                CAST((
                    SELECT COUNT(*)
                    FROM module_items mi2
                    WHERE mi2.module_id = mi.module_id
                ) AS integer) AS module_items_total
            FROM materials m
            INNER JOIN module_items mi
                ON mi.reference_id = m.id AND mi.item_type = 'Material'
            WHERE m.id = @MaterialId
              AND mi.module_id = @ModuleId;
            """;

        MaterialLookupRow? row = await connection.QueryFirstOrDefaultAsync<MaterialLookupRow>(
            new CommandDefinition(
                sql,
                new { query.MaterialId, query.ModuleId },
                cancellationToken: cancellationToken));

        if (row is null)
            return GeneralErrors.NotFound(query.MaterialId);

        return new MaterialDto(row.ModuleId, row.ModuleItemsTotal);
    }

    private sealed class MaterialLookupRow
    {
        public Guid ModuleId { get; init; }
        public int ModuleItemsTotal { get; init; }
    }
}
