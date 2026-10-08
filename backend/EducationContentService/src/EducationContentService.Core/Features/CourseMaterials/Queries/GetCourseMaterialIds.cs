using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Domain.Courses;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.CourseMaterials.Queries;

/// <summary>
///     Возвращает id-шники всех материалов, привязанных к курсу (без pagination).
///     Компактный endpoint для фронтового MaterialPicker'а — чтобы
///     фильтровать «уже прикреплённые» без подкачки полных DTO всех страниц.
/// </summary>
public sealed record GetCourseMaterialIdsQuery(Guid CourseId) : IQuery;

public sealed class GetCourseMaterialIdsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/{courseId:guid}/materials/ids",
                async Task<EndpointResult<IReadOnlyList<Guid>>> (
                    [FromRoute] Guid courseId,
                    [FromServices] GetCourseMaterialIdsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCourseMaterialIdsQuery(courseId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class GetCourseMaterialIdsHandler
    : IQueryHandlerWithResult<IReadOnlyList<Guid>, GetCourseMaterialIdsQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly ICoursesRepository _coursesRepository;
    private readonly UserScopedData _user;

    public GetCourseMaterialIdsHandler(
        ITransactionManager transactionManager,
        ICoursesRepository coursesRepository,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _coursesRepository = coursesRepository;
        _user = user;
    }

    public async Task<Result<IReadOnlyList<Guid>, Error>> Handle(
        GetCourseMaterialIdsQuery query,
        CancellationToken cancellationToken)
    {
        Result<Course, Error> course = await _coursesRepository.GetByAsync(
            x => x.Id == query.CourseId,
            cancellationToken);
        if (course.IsFailure)
            return course.Error;

        UnitResult<Error> ownership = _user.CheckOwnership(course.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT cm.material_id
            FROM course_materials cm
            WHERE cm.course_id = @CourseId
            """;

        IEnumerable<Guid> ids = await connection.QueryAsync<Guid>(
            new CommandDefinition(sql, new { query.CourseId }, cancellationToken: cancellationToken));

        IReadOnlyList<Guid> result = ids.ToList();
        return Result.Success<IReadOnlyList<Guid>, Error>(result);
    }
}
