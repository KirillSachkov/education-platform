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

public sealed record GetCourseLookupQuery(Guid CourseId) : IQuery;

public sealed class GetCourseLookupEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("internal/progress/courses/{courseId:guid}", async Task<EndpointResult<CourseDto>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCourseLookupHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCourseLookupQuery(courseId), cancellationToken))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetCourseLookupHandler : IQueryHandlerWithResult<CourseDto, GetCourseLookupQuery>
{
    private readonly ITransactionManager _transactionManager;

    public GetCourseLookupHandler(ITransactionManager transactionManager)
    {
        _transactionManager = transactionManager;
    }

    public async Task<Result<CourseDto, Error>> Handle(
        GetCourseLookupQuery query, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        // has_free_content — true если в курсе есть хотя бы один опубликованный
        // материал или задание с AccessType IN ('PUBLIC', 'REGISTERED') — то есть
        // free-to-view контент, открытый авторизованному пользователю без enrollment'а.
        // Issue #358: AccessType.FREE удалён; легаси-FREE сколлапсированы в REGISTERED.
        const string sql = """
                           SELECT
                               c.id,
                               c.author_id,
                               c.status,
                               (
                                   EXISTS (
                                       SELECT 1
                                       FROM materials m
                                       JOIN module_items mi ON mi.reference_id = m.id AND mi.item_type = 'Material'
                                       JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                                       WHERE ci.course_id = c.id
                                         AND m.access_type IN ('PUBLIC', 'REGISTERED')
                                         AND m.status = 'PUBLISHED'
                                   )
                                   OR EXISTS (
                                       SELECT 1
                                       FROM issues i
                                       JOIN module_items mi ON mi.reference_id = i.id AND mi.item_type = 'Issue'
                                       JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                                       WHERE ci.course_id = c.id
                                         AND i.access_type IN ('PUBLIC', 'REGISTERED')
                                         AND i.status = 'PUBLISHED'
                                   )
                               ) AS has_free_content
                           FROM courses c
                           WHERE c.id = @CourseId;
                           """;

        var row = await connection.QueryFirstOrDefaultAsync<CourseLookupRow>(sql, new { query.CourseId });

        if (row is null)
            return GeneralErrors.NotFound(query.CourseId);

        return new CourseDto(row.Id, row.AuthorId, row.Status, row.HasFreeContent);
    }

    private sealed class CourseLookupRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Status { get; init; } = null!;
        public bool HasFreeContent { get; init; }
    }
}
