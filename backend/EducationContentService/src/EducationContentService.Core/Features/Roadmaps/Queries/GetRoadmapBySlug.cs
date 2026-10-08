using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Roadmaps;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Roadmaps.Queries;

public sealed record GetRoadmapBySlugQuery(string Slug) : IQuery;

public sealed class GetRoadmapBySlugEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("roadmaps/by-slug/{slug}", async Task<EndpointResult<RoadmapDto>> (
                [FromRoute] string slug,
                [FromServices] GetRoadmapBySlugHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(new GetRoadmapBySlugQuery(slug), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetRoadmapBySlugHandler : IQueryHandlerWithResult<RoadmapDto, GetRoadmapBySlugQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetRoadmapBySlugHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<RoadmapDto, Error>> Handle(
        GetRoadmapBySlugQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               r.id,
                               r.author_id,
                               r.title,
                               r.description,
                               r.course_id,
                               r.slug,
                               r.status,
                               r.created_at,
                               r.updated_at
                           FROM roadmaps r
                           WHERE r.slug = @Slug AND r.status = 'PUBLISHED';

                           SELECT
                               n.id, n.node_type, n.position_x, n.position_y,
                               n.width, n.height, n.parent_node_id, n.data, n.sort_order
                           FROM roadmap_nodes n
                           JOIN roadmaps r ON r.id = n.roadmap_id
                           WHERE r.slug = @Slug AND r.status = 'PUBLISHED'
                           ORDER BY n.sort_order;

                           SELECT
                               e.id, e.source_node_id, e.target_node_id, e.label,
                               e.edge_type, e.animated, e.source_handle, e.target_handle
                           FROM roadmap_edges e
                           JOIN roadmaps r ON r.id = e.roadmap_id
                           WHERE r.slug = @Slug AND r.status = 'PUBLISHED';
                           """;

        var command = new CommandDefinition(sql, new { query.Slug }, cancellationToken: cancellationToken);
        await using SqlMapper.GridReader multi = await connection.QueryMultipleAsync(command);

        RoadmapRow? roadmapRow = await multi.ReadFirstOrDefaultAsync<RoadmapRow>();
        if (roadmapRow is null)
            return GeneralErrors.NotFound();

        var nodeRows = (await multi.ReadAsync<NodeRow>()).ToList();
        var edgeRows = (await multi.ReadAsync<EdgeRow>()).ToList();

        List<RoadmapNodeDto> nodes = nodeRows.Select(n => new RoadmapNodeDto(
            n.Id, n.NodeType, n.PositionX, n.PositionY, n.Width, n.Height,
            n.ParentNodeId, n.Data, n.SortOrder)).ToList();

        List<RoadmapEdgeDto> edges = edgeRows.Select(e => new RoadmapEdgeDto(
            e.Id, e.SourceNodeId, e.TargetNodeId,
            e.Label, e.EdgeType, e.Animated, e.SourceHandle, e.TargetHandle)).ToList();

        return new RoadmapDto(
            roadmapRow.Id, roadmapRow.AuthorId, roadmapRow.Title, roadmapRow.Description,
            roadmapRow.CourseId, roadmapRow.Slug, roadmapRow.Status,
            roadmapRow.CreatedAt, roadmapRow.UpdatedAt, nodes, edges);
    }

    private sealed class RoadmapRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public Guid? CourseId { get; init; }
        public string? Slug { get; init; }
        public string Status { get; init; } = null!;
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class NodeRow
    {
        public Guid Id { get; init; }
        public string NodeType { get; init; } = null!;
        public double PositionX { get; init; }
        public double PositionY { get; init; }
        public double? Width { get; init; }
        public double? Height { get; init; }
        public Guid? ParentNodeId { get; init; }
        public string Data { get; init; } = null!;
        public int SortOrder { get; init; }
    }

    private sealed class EdgeRow
    {
        public Guid Id { get; init; }
        public Guid SourceNodeId { get; init; }
        public Guid TargetNodeId { get; init; }
        public string? Label { get; init; }
        public string EdgeType { get; init; } = null!;
        public bool Animated { get; init; }
        public string? SourceHandle { get; init; }
        public string? TargetHandle { get; init; }
    }
}
