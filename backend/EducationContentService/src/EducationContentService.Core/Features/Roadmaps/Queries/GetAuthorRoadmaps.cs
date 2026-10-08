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

namespace EducationContentService.Core.Features.Roadmaps.Queries;

public sealed record GetAuthorRoadmapsQuery(Guid AuthorId, string? Cursor, int Limit) : IQuery;

public sealed class GetAuthorRoadmapsQueryValidator : AbstractValidator<GetAuthorRoadmapsQuery>
{
    public GetAuthorRoadmapsQueryValidator()
    {
        RuleFor(x => x.AuthorId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetAuthorRoadmapsQuery.AuthorId)));

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, 100)
            .WithError(GeneralErrors.ValueIsInvalid(nameof(GetAuthorRoadmapsQuery.Limit)));
    }
}

public sealed class GetAuthorRoadmapsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("roadmaps/by-author/{authorId:guid}",
                async Task<EndpointResult<CursorResponse<RoadmapSummaryDto>>> (
                    [FromRoute] Guid authorId,
                    [FromQuery] string? cursor,
                    [FromQuery] int limit,
                    [FromServices] GetAuthorRoadmapsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new GetAuthorRoadmapsQuery(authorId, cursor, limit == 0 ? 20 : limit),
                    cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetAuthorRoadmapsHandler
    : IQueryHandlerWithResult<CursorResponse<RoadmapSummaryDto>, GetAuthorRoadmapsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetAuthorRoadmapsQuery> _validator;

    public GetAuthorRoadmapsHandler(
        ITransactionManager transactionManager,
        IValidator<GetAuthorRoadmapsQuery> validator)
    {
        _transactionManager = transactionManager;
        _validator = validator;
    }

    public async Task<Result<CursorResponse<RoadmapSummaryDto>, Error>> Handle(
        GetAuthorRoadmapsQuery query, CancellationToken cancellationToken = default)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Cursor? cursor = Cursor.Decode(query.Cursor);
        DbConnection connection = _transactionManager.GetDbConnection();

        var parameters = new
        {
            query.AuthorId,
            CursorCreatedAt = cursor?.CreatedAt,
            CursorId = cursor?.LastId,
            Limit = query.Limit + 1,
        };

        const string countSql = """
                                SELECT COUNT(*)
                                FROM roadmaps r
                                WHERE r.author_id = @AuthorId
                                  AND r.status = 'PUBLISHED';
                                """;

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
                                   COALESCE(nc.node_count, 0) AS node_count
                               FROM roadmaps r
                               LEFT JOIN (
                                   SELECT roadmap_id, COUNT(*) AS node_count
                                   FROM roadmap_nodes
                                   GROUP BY roadmap_id
                               ) nc ON nc.roadmap_id = r.id
                               WHERE r.author_id = @AuthorId
                                 AND r.status = 'PUBLISHED'
                                 AND (@CursorCreatedAt IS NULL OR (r.updated_at, r.id) < (@CursorCreatedAt, @CursorId))
                               ORDER BY r.updated_at DESC, r.id DESC
                               LIMIT @Limit;
                               """;

        long totalCount = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));

        List<RoadmapListRow> rows = (await connection.QueryAsync<RoadmapListRow>(
            new CommandDefinition(dataSql, parameters, cancellationToken: cancellationToken))).ToList();

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
            TotalCount = totalCount,
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
