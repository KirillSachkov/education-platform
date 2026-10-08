using Core.Abstractions;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Responses;
using ProgressService.Core.Features.QuizAttempts.Admin;
using ProgressService.Domain;

namespace ProgressService.Core.Features.Courses.Stats;

public sealed record GetCourseQuizStatsDetailQuery(Guid CourseId, Guid QuizId) : IQuery;

public sealed class GetCourseQuizStatsDetailEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/progress/courses/{courseId:guid}/stats/quizzes/{quizId:guid}/",
                async Task<EndpointResult<QuizAdminStatsResponse>> (
                    [FromRoute] Guid courseId,
                    [FromRoute] Guid quizId,
                    [FromServices] GetCourseQuizStatsDetailHandler handler,
                    CancellationToken ct) =>
                await handler.Handle(new GetCourseQuizStatsDetailQuery(courseId, quizId), ct))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
}

/// <summary>
///     Author drill-in по одному тесту курса (#634): распределение баллов + correct-rate
///     по вопросам. После ownership-гейта (владелец курса / admin) проверяет, что
///     <c>quizId ∈ blueprint.QuizIds</c> (anti-IDOR: автор не может смотреть чужой тест
///     вне своего курса — зеркало двусторонней проверки ECS <c>AttachQuizToModule</c>),
///     затем делегирует расчёт существующему <see cref="GetQuizAdminStatsHandler"/> —
///     та же логика грейдинга/бакетов, что и в глобальной админ-аналитике, без дублирования.
/// </summary>
public sealed class GetCourseQuizStatsDetailHandler
    : IQueryHandlerWithResult<QuizAdminStatsResponse, GetCourseQuizStatsDetailQuery>
{
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly UserScopedData _user;
    private readonly GetQuizAdminStatsHandler _quizAdminStatsHandler;

    public GetCourseQuizStatsDetailHandler(
        IEducationContentServiceClient educationContentServiceClient,
        UserScopedData user,
        GetQuizAdminStatsHandler quizAdminStatsHandler)
    {
        _educationContentServiceClient = educationContentServiceClient;
        _user = user;
        _quizAdminStatsHandler = quizAdminStatsHandler;
    }

    public async Task<Result<QuizAdminStatsResponse, Error>> Handle(
        GetCourseQuizStatsDetailQuery query,
        CancellationToken cancellationToken)
    {
        Result<CourseDto, Error> authResult = await CourseStatsAuthorization.AuthorizeCourseManagementAsync(
            _educationContentServiceClient, _user, query.CourseId, cancellationToken);
        if (authResult.IsFailure)
        {
            return authResult.Error;
        }

        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintResult =
            await _educationContentServiceClient.GetCourseProgressBlueprintsAsync(
                new GetCourseProgressBlueprintsRequest([query.CourseId]),
                cancellationToken);
        if (blueprintResult.IsFailure)
        {
            return ProgressErrors.EducationContentServiceUnavailable();
        }

        CourseProgressBlueprintDto? blueprint = blueprintResult.Value.FirstOrDefault();
        bool quizInCourse = blueprint is not null && blueprint.QuizIds.Contains(query.QuizId);
        if (!quizInCourse)
        {
            return ProgressErrors.QuizNotInCourse();
        }

        return await _quizAdminStatsHandler.Handle(new GetQuizAdminStatsQuery(query.QuizId), cancellationToken);
    }
}
