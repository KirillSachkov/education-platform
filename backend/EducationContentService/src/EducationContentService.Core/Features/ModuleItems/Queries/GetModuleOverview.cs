using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Modules;
using EducationContentService.Domain.Modules;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ModuleItems.Queries;

public sealed record GetModuleOverviewQuery(Guid ModuleId) : IQuery;

public sealed class GetModuleOverviewEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("modules/{moduleId:guid}/overview", async Task<EndpointResult<ModuleOverviewDto>> (
            [FromRoute] Guid moduleId,
            [FromServices] GetModuleOverviewHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetModuleOverviewQuery(moduleId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetModuleOverviewHandler : IQueryHandlerWithResult<ModuleOverviewDto, GetModuleOverviewQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetModuleOverviewHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<ModuleOverviewDto, Error>> Handle(
        GetModuleOverviewQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                m.id,
                m.title,
                m.description,
                m.detailed_description
            FROM modules m
            WHERE m.id = @ModuleId AND m.status = 'PUBLISHED';

            SELECT
                mi.reference_id AS id,
                mi.item_type,
                COALESCE(mat.title, i.title, q.title) AS title,
                mi.is_optional,
                mi.view_priority
            FROM module_items mi
            LEFT JOIN materials mat ON mi.item_type = 'Material' AND mi.reference_id = mat.id
            LEFT JOIN issues i ON mi.item_type = 'Issue' AND mi.reference_id = i.id
            LEFT JOIN quizzes q ON mi.item_type = 'Quiz' AND mi.reference_id = q.id
            WHERE mi.module_id = @ModuleId
              AND COALESCE(mat.status, i.status, q.status) = 'PUBLISHED'
            ORDER BY mi.sort_key;

            SELECT DISTINCT c.id
            FROM course_items ci
            JOIN courses c ON c.id = ci.course_id
            WHERE ci.reference_id = @ModuleId
              AND ci.item_type = 'Module'
              AND c.status = 'PUBLISHED';
            """;

        var command = new CommandDefinition(sql, new { query.ModuleId }, cancellationToken: cancellationToken);
        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(command);

        ModuleOverviewRow? moduleRow = await multi.ReadFirstOrDefaultAsync<ModuleOverviewRow>();
        if (moduleRow is null)
            return GeneralErrors.NotFound(query.ModuleId);

        var itemRows = (await multi.ReadAsync<ModuleOverviewItemRow>()).ToList();

        // После унификации Material is_public больше не применяется — Access контролирует AccessType.
        int lessonCount = itemRows.Count(r => string.Equals(r.ItemType, nameof(ModuleItemType.Material), StringComparison.Ordinal));
        int issueCount = itemRows.Count(r => string.Equals(r.ItemType, nameof(ModuleItemType.Issue), StringComparison.Ordinal));

        List<ModuleOverviewItemDto> items = itemRows
            .Where(r => r.Title is not null)
            .Select(r => new ModuleOverviewItemDto(r.Id, r.ItemType, r.Title!, r.IsOptional, r.ViewPriority))
            .ToList();

        return new ModuleOverviewDto(
            moduleRow.Id,
            moduleRow.Title,
            moduleRow.Description,
            moduleRow.DetailedDescription,
            lessonCount,
            issueCount,
            items);
    }

    private sealed class ModuleOverviewRow
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public string? DetailedDescription { get; init; }
    }

    private sealed class ModuleOverviewItemRow
    {
        public Guid Id { get; init; }
        public string ItemType { get; init; } = null!;
        public string? Title { get; init; }
        public bool IsOptional { get; init; }
        public string ViewPriority { get; init; } = null!;
    }

}
