using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Dtos;
using ProgressService.Domain;

namespace ProgressService.Core.Features.Courses.Queries;

public sealed record GetStudentCourseProgressQuery(Guid CourseId, Guid UserId) : IQuery;

public sealed class GetStudentCourseProgressQueryValidator : AbstractValidator<GetStudentCourseProgressQuery>
{
    public GetStudentCourseProgressQueryValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetStudentCourseProgressQuery.CourseId)));

        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetStudentCourseProgressQuery.UserId)));
    }
}

public sealed class GetStudentCourseProgressEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/courses/{courseId:guid}/students/{userId:guid}/progress",
                async Task<EndpointResult<StudentCourseProgressDto>> (
                        [FromRoute] Guid courseId,
                        [FromRoute] Guid userId,
                        [FromServices] GetStudentCourseProgressHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new GetStudentCourseProgressQuery(courseId, userId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

/// <summary>
///     Per-student progress detail для staff (автор курса / админ / модератор). Отдаёт ТОЛЬКО
///     прогресс-факты ProgressService (изученные материалы + статусы заданий target-юзера) — структуру
///     курса фронт уже знает (course-builder DTO) и накладывает статусы сверху.
///     Авторизация зеркалит <see cref="GetCourseStudentsHandler"/>: ADMIN|MODERATOR проходят всегда,
///     AUTHOR — только владелец курса (резолв авторства через ECS lookup).
///     access-derive-model (#367): enrollment ленивый. Если у target-юзера нет прогресс-якоря
///     (grant-holder, ещё не начинал) → 200 с пустыми массивами (`enrollmentStarted=false`), НЕ 404.
/// </summary>
public sealed class GetStudentCourseProgressHandler
    : IQueryHandlerWithResult<StudentCourseProgressDto, GetStudentCourseProgressQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly UserScopedData _user;
    private readonly IValidator<GetStudentCourseProgressQuery> _validator;

    public GetStudentCourseProgressHandler(
        ITransactionManager transactionManager,
        IEducationContentServiceClient educationContentServiceClient,
        UserScopedData user,
        IValidator<GetStudentCourseProgressQuery> validator)
    {
        _transactionManager = transactionManager;
        _educationContentServiceClient = educationContentServiceClient;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<StudentCourseProgressDto, Error>> Handle(
        GetStudentCourseProgressQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Result<CourseDto, Error> courseResult = await _educationContentServiceClient.GetCourseLookupAsync(
            query.CourseId,
            cancellationToken);
        if (courseResult.IsFailure)
        {
            return courseResult.Error;
        }

        bool isPrivileged = _user.HasRole(PlatformRoles.ADMIN) || _user.HasRole(PlatformRoles.MODERATOR);
        bool isAuthorOwner = _user.HasRole(PlatformRoles.AUTHOR) && courseResult.Value.AuthorId == _user.UserId;
        if (!isPrivileged && !isAuthorOwner)
        {
            return ProgressErrors.CourseManagementForbidden(query.CourseId);
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        // Прогресс-якорь target-юзера в курсе. Нет строки → grant-holder ещё не начинал:
        // 200 с пустыми массивами (#367), а не 404 — иначе вкладка прогресса grant-holder'а
        // выглядела бы как ошибка до первого engagement'а.
        const string enrollmentSql = """
                                     SELECT ce.enrolled_at
                                     FROM course_enrollments ce
                                     WHERE ce.user_id = @UserId
                                       AND ce.course_id = @CourseId
                                     LIMIT 1;
                                     """;

        DateTime? enrolledAt = await connection.QueryFirstOrDefaultAsync<DateTime?>(
            new CommandDefinition(
                enrollmentSql,
                new { query.UserId, query.CourseId },
                cancellationToken: cancellationToken));

        if (enrolledAt is null)
        {
            return new StudentCourseProgressDto(
                query.CourseId,
                query.UserId,
                EnrollmentStarted: false,
                EnrolledAt: null,
                CompletedMaterials: [],
                Issues: []);
        }

        // Blueprint из ECS даёт полный список material_id курса (модули + Лента + подборки),
        // которым мы скоупим user-scoped material_views (просмотр чужого материала не должен течь сюда).
        Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error> blueprintResult =
            await _educationContentServiceClient.GetCourseProgressBlueprintsAsync(
                new GetCourseProgressBlueprintsRequest([query.CourseId]),
                cancellationToken);
        if (blueprintResult.IsFailure)
        {
            return blueprintResult.Error;
        }

        CourseProgressBlueprintDto? blueprint = blueprintResult.Value.FirstOrDefault();
        Guid[] materialIds = blueprint is null
            ? []
            : blueprint.MaterialIds as Guid[] ?? blueprint.MaterialIds.ToArray();

        List<StudentCompletedMaterialDto> completedMaterials = [];
        if (materialIds.Length > 0)
        {
            // is_completed = TRUE — только явно отмеченные «Изучено» (issue #285). Silent track-view'ы
            // (mount detail-страницы) сюда НЕ попадают.
            const string materialsSql = """
                                        SELECT
                                            mv.material_id AS MaterialId,
                                            COALESCE(mv.completed_at, mv.viewed_at) AS CompletedAt
                                        FROM material_views mv
                                        WHERE mv.user_id = @UserId
                                          AND mv.material_id = ANY(@MaterialIds)
                                          AND mv.is_completed = TRUE;
                                        """;

            completedMaterials = (await connection.QueryAsync<StudentCompletedMaterialDto>(
                new CommandDefinition(
                    materialsSql,
                    new { query.UserId, MaterialIds = materialIds },
                    cancellationToken: cancellationToken))).ToList();
        }

        // Статусы заданий студента: issue_progress + последняя попытка (review_status / submitted_at)
        // + общее число попыток. Скоупим по enrollment'ам пары (user, course).
        const string issuesSql = """
                                 SELECT
                                     ip.issue_id AS IssueId,
                                     ip.project_id AS ProjectId,
                                     ip.status AS Status,
                                     latest.review_status AS ReviewStatus,
                                     latest.submitted_at AS SubmittedAt,
                                     COALESCE(counts.attempts_count, 0) AS AttemptsCount
                                 FROM issue_progress ip
                                 JOIN course_enrollments ce ON ce.id = ip.enrollment_id
                                 LEFT JOIN LATERAL (
                                     SELECT s.review_status, s.submitted_at
                                     FROM issue_submissions s
                                     WHERE s.issue_progress_id = ip.id
                                     ORDER BY s.attempt_number DESC, s.submitted_at DESC
                                     LIMIT 1
                                 ) latest ON TRUE
                                 LEFT JOIN LATERAL (
                                     SELECT COUNT(*)::integer AS attempts_count
                                     FROM issue_submissions s
                                     WHERE s.issue_progress_id = ip.id
                                 ) counts ON TRUE
                                 WHERE ce.user_id = @UserId
                                   AND ce.course_id = @CourseId;
                                 """;

        List<StudentIssueProgressDto> issues = (await connection.QueryAsync<StudentIssueProgressDto>(
            new CommandDefinition(
                issuesSql,
                new { query.UserId, query.CourseId },
                cancellationToken: cancellationToken))).ToList();

        return new StudentCourseProgressDto(
            query.CourseId,
            query.UserId,
            EnrollmentStarted: true,
            EnrolledAt: enrolledAt,
            completedMaterials,
            issues);
    }
}
