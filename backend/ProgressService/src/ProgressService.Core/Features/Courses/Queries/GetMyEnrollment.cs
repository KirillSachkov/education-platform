using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Core.Validation;
using Dapper;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Dtos;
using ProgressService.Domain.Enrollments;

namespace ProgressService.Core.Features.Courses.Queries;

public sealed record GetMyEnrollmentQuery(Guid CourseId) : IQuery;

public sealed class GetMyEnrollmentQueryValidator : AbstractValidator<GetMyEnrollmentQuery>
{
    public GetMyEnrollmentQueryValidator()
    {
        RuleFor(x => x.CourseId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired(nameof(GetMyEnrollmentQuery.CourseId)));
    }
}

public sealed class GetMyEnrollmentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/courses/{courseId:guid}/my-enrollment",
                async Task<EndpointResult<CourseEnrollmentProgressDto?>> (
                        [FromRoute] Guid courseId,
                        [FromServices] GetMyEnrollmentHandler handler,
                        CancellationToken cancellationToken) =>
                    await handler.Handle(new GetMyEnrollmentQuery(courseId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Content.VIEW);
    }
}

public sealed class GetMyEnrollmentHandler
    : IQueryHandlerWithResult<CourseEnrollmentProgressDto?, GetMyEnrollmentQuery>
{
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<GetMyEnrollmentQuery> _validator;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly UserScopedData _user;

    public GetMyEnrollmentHandler(
        ITransactionManager transactionManager,
        IValidator<GetMyEnrollmentQuery> validator,
        IEntitlementChecker entitlementChecker,
        UserScopedData user)
    {
        _transactionManager = transactionManager;
        _validator = validator;
        _entitlementChecker = entitlementChecker;
        _user = user;
    }

    public async Task<Result<CourseEnrollmentProgressDto?, Error>> Handle(
        GetMyEnrollmentQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                           SELECT
                               ce.id AS Id,
                               ce.course_id AS CourseId,
                               ce.source AS Source,
                               ce.enrolled_at AS EnrolledAt
                           FROM course_enrollments ce
                           WHERE ce.user_id = @UserId AND ce.course_id = @CourseId
                           LIMIT 1
                           """;

        // MaterialsTotal/MaterialsViewed здесь — «материалы, с которыми пользователь
        // взаимодействовал в рамках этого enrollment» (module_item_progress создаётся
        // лениво при просмотре). Для точного счётчика «всего материалов в курсе»
        // фронт использует blueprint из GetCourseLearningState/course detail.
        const string progressSql = """
                                   SELECT
                                       COUNT(*) FILTER (WHERE item_type = 'MATERIAL') AS MaterialsTotal,
                                       COUNT(*) FILTER (WHERE item_type = 'MATERIAL' AND status = 'COMPLETED') AS MaterialsViewed
                                   FROM module_item_progress WHERE enrollment_id = @EnrollmentId;

                                   SELECT COUNT(*) AS ModulesTotal, COUNT(*) FILTER (WHERE status = 'COMPLETED') AS ModulesCompleted
                                   FROM module_progress WHERE enrollment_id = @EnrollmentId;

                                   SELECT COUNT(*) AS IssuesTotal, COUNT(*) FILTER (WHERE status = 'COMPLETED') AS IssuesCompleted
                                   FROM issue_progress WHERE enrollment_id = @EnrollmentId;
                                   """;

        EnrollmentRow? enrollment = await connection.QuerySingleOrDefaultAsync<EnrollmentRow>(
            new CommandDefinition(
                sql,
                new { UserId = _user.UserId, CourseId = query.CourseId },
                cancellationToken: cancellationToken));

        if (enrollment is null)
        {
            // Derive-модель (epic access-derive-model, Phase 1): нет прогресс-якоря, но
            // пользователь может быть entitled (grant) и ещё не начинал. Тогда отдаём
            // synthetic zeroed enrollment (Source=ACCESS_PLAN_GRANT, EnrollmentId=Guid.Empty),
            // а не null — чтобы grant-holder без engagement не выглядел как «не записан».
            AccessDecision access = await _entitlementChecker.CheckAccessAsync(
                _user.ToAccessSubject(),
                ResourceTypes.COURSE,
                query.CourseId,
                cancellationToken);

            if (!access.IsGranted)
            {
                return Result.Success<CourseEnrollmentProgressDto?, Error>(null);
            }

            return Result.Success<CourseEnrollmentProgressDto?, Error>(
                new CourseEnrollmentProgressDto(
                    Guid.Empty,
                    query.CourseId,
                    Source: nameof(EnrollmentSource.ACCESS_PLAN_GRANT),
                    MaterialsTotal: 0,
                    MaterialsViewed: 0,
                    ModulesTotal: 0,
                    ModulesCompleted: 0,
                    IssuesTotal: 0,
                    IssuesCompleted: 0,
                    EnrolledAt: default));
        }

        await using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(
                progressSql,
                new { EnrollmentId = enrollment.Id },
                cancellationToken: cancellationToken));

        MaterialCountsRow materialCounts = await multi.ReadSingleAsync<MaterialCountsRow>();
        ModuleCountsRow moduleCounts = await multi.ReadSingleAsync<ModuleCountsRow>();
        IssueCountsRow issueCounts = await multi.ReadSingleAsync<IssueCountsRow>();

        return Result.Success<CourseEnrollmentProgressDto?, Error>(
            new CourseEnrollmentProgressDto(
                enrollment.Id,
                enrollment.CourseId,
                enrollment.Source,
                (int)materialCounts.MaterialsTotal,
                (int)materialCounts.MaterialsViewed,
                (int)moduleCounts.ModulesTotal,
                (int)moduleCounts.ModulesCompleted,
                (int)issueCounts.IssuesTotal,
                (int)issueCounts.IssuesCompleted,
                enrollment.EnrolledAt));
    }

    private sealed class EnrollmentRow
    {
        public Guid Id { get; init; }
        public Guid CourseId { get; init; }
        public string Source { get; init; } = null!;
        public DateTime EnrolledAt { get; init; }
    }

    private sealed class MaterialCountsRow
    {
        public long MaterialsTotal { get; init; }
        public long MaterialsViewed { get; init; }
    }

    private sealed class ModuleCountsRow
    {
        public long ModulesTotal { get; init; }
        public long ModulesCompleted { get; init; }
    }

    private sealed class IssueCountsRow
    {
        public long IssuesTotal { get; init; }
        public long IssuesCompleted { get; init; }
    }
}
