using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Materials;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.CourseMaterials.Queries;

/// <summary>
///     Теги, реально используемые опубликованными материалами конкретного курса.
///     Metadata-only read для фильтра базы знаний курса.
/// </summary>
public sealed record GetCourseMaterialTagsQuery(Guid CourseId) : IQuery;

public sealed class GetCourseMaterialTagsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/{courseId:guid}/materials/tags",
                async Task<EndpointResult<IReadOnlyList<CourseMaterialTagDto>>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCourseMaterialTagsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCourseMaterialTagsQuery(courseId), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetCourseMaterialTagsHandler
    : IQueryHandlerWithResult<IReadOnlyList<CourseMaterialTagDto>, GetCourseMaterialTagsQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetCourseMaterialTagsHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<IReadOnlyList<CourseMaterialTagDto>, Error>> Handle(
        GetCourseMaterialTagsQuery query,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT DISTINCT
                t.id,
                t.title,
                t.slug,
                LOWER(t.kind) AS kind
            FROM course_materials cm
            JOIN courses c ON c.id = cm.course_id AND c.status = 'PUBLISHED'
            JOIN materials m ON m.id = cm.material_id
            JOIN tags.entity_tags et ON et.entity_type = 'Material' AND et.entity_id = m.id
            JOIN tags.tags t ON t.id = et.tag_id
            WHERE cm.course_id = @CourseId
              AND m.status = 'PUBLISHED'
              AND m.published_at IS NOT NULL
              AND t.kind = 'CANON'
            ORDER BY t.title;
            """;

        IEnumerable<CourseMaterialTagDto> tags = await connection.QueryAsync<CourseMaterialTagDto>(
            new CommandDefinition(sql, new { query.CourseId }, cancellationToken: cancellationToken));

        return Result.Success<IReadOnlyList<CourseMaterialTagDto>, Error>(tags.ToList());
    }
}
