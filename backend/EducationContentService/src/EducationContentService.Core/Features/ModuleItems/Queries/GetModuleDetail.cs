using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Modules;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.ModuleItems.Queries;

public sealed record GetModuleDetailQuery(Guid ModuleId) : IQuery;

public sealed class GetModuleDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("modules/{moduleId:guid}/detail", async Task<EndpointResult<ModuleDetailDto>> (
            [FromRoute] Guid moduleId,
            [FromServices] GetModuleDetailHandler handler,
            CancellationToken cancellationToken) =>
            await handler.Handle(new GetModuleDetailQuery(moduleId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class GetModuleDetailHandler : IQueryHandlerWithResult<ModuleDetailDto, GetModuleDetailQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetModuleDetailHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<ModuleDetailDto, Error>> Handle(
        GetModuleDetailQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                m.id,
                m.author_id,
                m.title,
                m.description,
                m.detailed_description,
                ci.course_id,
                m.status,
                m.created_at,
                m.updated_at
            FROM modules m
            LEFT JOIN course_items ci
                ON ci.reference_id = m.id
               AND ci.item_type = 'Module'
            WHERE m.id = @ModuleId;

            SELECT
                mi.id,
                mi.reference_id,
                mi.item_type,
                mi.sort_key,
                mi.is_optional,
                mi.view_priority,
                COALESCE(mat.title, i.title, q.title) AS title,
                COALESCE(mat.status, i.status, q.status) AS status,
                COALESCE(mat.access_type, i.access_type, q.access_type) AS access_type
            FROM module_items mi
            LEFT JOIN materials mat ON mi.item_type = 'Material' AND mi.reference_id = mat.id
            LEFT JOIN issues i ON mi.item_type = 'Issue' AND mi.reference_id = i.id
            LEFT JOIN quizzes q ON mi.item_type = 'Quiz' AND mi.reference_id = q.id
            WHERE mi.module_id = @ModuleId
            ORDER BY mi.sort_key;
            """;

        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, new { query.ModuleId }, cancellationToken: cancellationToken));

        ModuleDetailRow? moduleRow = await multi.ReadFirstOrDefaultAsync<ModuleDetailRow>();
        if (moduleRow == null)
            return GeneralErrors.NotFound(query.ModuleId);

        IEnumerable<ModuleItemRow> itemRows = await multi.ReadAsync<ModuleItemRow>();

        List<ModuleItemDto> items = itemRows.Select(i => new ModuleItemDto(
            i.Id, i.ReferenceId, i.ItemType, i.SortKey, i.IsOptional, i.ViewPriority, i.Title, i.Status, i.AccessType)).ToList();

        return new ModuleDetailDto(
            moduleRow.Id,
            moduleRow.AuthorId,
            moduleRow.Title,
            moduleRow.Description,
            moduleRow.DetailedDescription,
            moduleRow.CourseId,
            moduleRow.Status,
            moduleRow.CreatedAt,
            moduleRow.UpdatedAt,
            items);
    }

    private sealed class ModuleDetailRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public string? DetailedDescription { get; init; }
        public Guid? CourseId { get; init; }
        public string Status { get; init; } = null!;
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class ModuleItemRow
    {
        public Guid Id { get; init; }
        public Guid ReferenceId { get; init; }
        public string ItemType { get; init; } = null!;
        public string SortKey { get; init; } = null!;
        public bool IsOptional { get; init; }
        public string ViewPriority { get; init; } = null!;
        public string? Title { get; init; }
        public string? Status { get; init; }
        public string? AccessType { get; init; }
    }
}
