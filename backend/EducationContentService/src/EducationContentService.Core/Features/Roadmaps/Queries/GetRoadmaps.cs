using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Roadmaps;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Roadmaps.Queries;

public sealed record GetRoadmapsQuery(
    Guid? CourseId,
    bool? StandaloneOnly,
    string? Cursor,
    int Limit = 20) : IQuery;

public sealed class GetRoadmapsQueryValidator : AbstractValidator<GetRoadmapsQuery>
{
    public GetRoadmapsQueryValidator()
    {
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetRoadmapsQuery.Limit)));
    }
}

public sealed class GetRoadmapsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("roadmaps", async Task<EndpointResult<CursorResponse<RoadmapSummaryDto>>> (
                [FromQuery] Guid? courseId,
                [FromQuery] bool? standaloneOnly,
                [FromQuery] string? cursor,
                [FromQuery] int? limit,
                [FromServices] GetRoadmapsHandler handler,
                CancellationToken cancellationToken) =>
            await handler.Handle(
                new GetRoadmapsQuery(courseId, standaloneOnly, cursor, limit is null or 0 ? 20 : limit.Value),
                cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class GetRoadmapsHandler
    : IQueryHandlerWithResult<CursorResponse<RoadmapSummaryDto>, GetRoadmapsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetRoadmapsQuery> _validator;
    private readonly UserScopedData _user;

    public GetRoadmapsHandler(
        ITransactionManager transactionManager,
        IValidator<GetRoadmapsQuery> validator,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _validator = validator;
        _user = user;
    }

    public async Task<Result<CursorResponse<RoadmapSummaryDto>, Error>> Handle(
        GetRoadmapsQuery query, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Cursor? cursor = Cursor.Decode(query.Cursor);

        DbConnection connection = _transactionManager.GetDbConnection();

        var parameters = new
        {
            query.CourseId,
            query.StandaloneOnly,
            UserId = _user.UserId,
            CursorCreatedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = query.Limit + 1
        };

        const string dataSql = """
                                SELECT
                                    r.id,
                                    r.author_id,
                                    r.title,
                                    r.description,
                                    r.course_id,
                                    r.slug,
                                    r.status,
                                    r.created_at,
                                    r.updated_at,
                                    COALESCE(nc.node_count, 0) AS node_count,
                                    COUNT(*) OVER () AS total_count
                                FROM roadmaps r
                                LEFT JOIN (
                                    SELECT roadmap_id, COUNT(*) AS node_count
                                    FROM roadmap_nodes
                                    GROUP BY roadmap_id
                                ) nc ON nc.roadmap_id = r.id
                                WHERE (@CourseId IS NULL OR r.course_id = @CourseId)
                                  AND (@StandaloneOnly IS NOT TRUE OR r.course_id IS NULL)
                                  AND (r.author_id = @UserId OR r.status = 'PUBLISHED')
                                  AND (@CursorCreatedAt IS NULL OR (r.updated_at, r.id) < (@CursorCreatedAt, @CursorId))
                                ORDER BY r.updated_at DESC, r.id DESC
                                LIMIT @Limit;
                                """;

        long totalCount = 0;
        List<RoadmapListRow> rows = (await connection.QueryAsync<RoadmapListRow, long, RoadmapListRow>(
            sql: dataSql,
            map: (row, count) =>
            {
                totalCount = count;
                return row;
            },
            param: parameters,
            splitOn: "total_count")).ToList();

        bool hasMore = rows.Count > query.Limit;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        string? nextCursor = hasMore
            ? Cursor.Encode(rows[^1].UpdatedAt, rows[^1].Id)
            : null;

        IReadOnlyList<RoadmapSummaryDto> items = rows.Select(r => new RoadmapSummaryDto(
            r.Id, r.AuthorId, r.Title, r.Description, r.CourseId, r.Slug,
            r.Status, r.NodeCount, r.CreatedAt, r.UpdatedAt)).ToList();

        return new CursorResponse<RoadmapSummaryDto>
        {
            Items = items,
            NextCursor = nextCursor,
            TotalCount = totalCount
        };
    }

    private sealed class RoadmapListRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public Guid? CourseId { get; init; }
        public string? Slug { get; init; }
        public string Status { get; init; } = null!;
        public int NodeCount { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }
}
