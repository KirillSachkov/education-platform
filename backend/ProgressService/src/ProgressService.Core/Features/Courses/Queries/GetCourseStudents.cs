using System.Data;
using System.Data.Common;
using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using AccessService.Contracts.HttpCommunication;
using AccessService.Contracts.PlanGrants.Dtos;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Requests;
using ProgressService.Domain;

namespace ProgressService.Core.Features.Courses.Queries;

public sealed record GetCourseStudentsQuery(GetCourseStudentsRequest Request) : IQuery;

public sealed class GetCourseStudentsQueryValidator : AbstractValidator<GetCourseStudentsQuery>
{
    public GetCourseStudentsQueryValidator()
    {
        RuleFor(x => x.Request.CourseId)
            .NotEmpty();

        RuleFor(x => x.Request.Page)
            .GreaterThan(0)
            .WithError(GeneralErrors.ValueIsInvalid("page"));

        RuleFor(x => x.Request.PageSize)
            .GreaterThan(0)
            .LessThanOrEqualTo(100)
            .WithError(GeneralErrors.ValueIsInvalid("pageSize"));
    }
}

public sealed class GetCourseStudentsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/progress/courses/{courseId:guid}/students",
                async Task<EndpointResult<PaginationResponse<CourseStudentDto>>> (
                    [AsParameters] GetCourseStudentsRequest request,
                    [FromServices] GetCourseStudentsHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCourseStudentsQuery(request), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

/// <summary>
/// Derive-модель (epic access-derive-model, Phase 1): ростер курса = grant-holders
/// из AccessService (<see cref="IAccessServiceClient.GetCourseGranteesAsync"/>), а НЕ
/// материализованные <c>course_enrollments</c> строки. Это чинит баг «пустая вкладка
/// Студенты» на новом курсе — lifetime grant-holder появляется сразу, до любого engagement.
/// Локальный прогресс (enrollmentId / enrolled_at) джойнится LEFT JOIN'ом
/// по userId; для grant-holder'а без прогресс-строки эти поля синтетические (enrolledAt =
/// grant.GrantedAt). Профиль обогащается через AuthService. Name-search: matching userIds
/// сначала резолвятся через AuthService, потом передаются как userIdsFilter в grantees-запрос.
/// </summary>
public sealed class GetCourseStudentsHandler
    : IQueryHandlerWithResult<PaginationResponse<CourseStudentDto>, GetCourseStudentsQuery>
{
    private const int GRANTEES_PAGE_LIMIT = 100;

    private readonly ITransactionManager _transactionManager;
    private readonly IAuthServiceClient _authServiceClient;
    private readonly IEducationContentServiceClient _educationContentServiceClient;
    private readonly IAccessServiceClient _accessServiceClient;
    private readonly UserScopedData _user;
    private readonly IValidator<GetCourseStudentsQuery> _validator;

    public GetCourseStudentsHandler(
        ITransactionManager transactionManager,
        IAuthServiceClient authServiceClient,
        IEducationContentServiceClient educationContentServiceClient,
        IAccessServiceClient accessServiceClient,
        UserScopedData user,
        IValidator<GetCourseStudentsQuery> validator)
    {
        _transactionManager = transactionManager;
        _authServiceClient = authServiceClient;
        _educationContentServiceClient = educationContentServiceClient;
        _accessServiceClient = accessServiceClient;
        _user = user;
        _validator = validator;
    }

    public async Task<Result<PaginationResponse<CourseStudentDto>, Error>> Handle(
        GetCourseStudentsQuery query,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Result<CourseDto, Error> courseResult = await _educationContentServiceClient.GetCourseLookupAsync(
            query.Request.CourseId,
            cancellationToken);
        if (courseResult.IsFailure)
        {
            return courseResult.Error;
        }

        bool isPrivileged = _user.HasRole(PlatformRoles.ADMIN) || _user.HasRole(PlatformRoles.MODERATOR);
        bool isAuthorOwner = _user.HasRole(PlatformRoles.AUTHOR) && courseResult.Value.AuthorId == _user.UserId;
        if (!isPrivileged && !isAuthorOwner)
        {
            return ProgressErrors.CourseManagementForbidden(query.Request.CourseId);
        }

        Guid authorId = courseResult.Value.AuthorId;
        string searchTrimmed = query.Request.Search?.Trim() ?? "";

        Result<RenderedStudentsPage, Error> pageResult =
            await FetchCourseStudentsPage(query.Request, authorId, searchTrimmed, cancellationToken);

        if (pageResult.IsFailure)
        {
            return pageResult.Error;
        }

        RenderedStudentsPage page = pageResult.Value;
        return new PaginationResponse<CourseStudentDto>(
            page.Items,
            page.TotalCount,
            query.Request.Page,
            query.Request.PageSize,
            page.TotalPages);
    }

    private async Task<Result<RenderedStudentsPage, Error>> FetchCourseStudentsPage(
        GetCourseStudentsRequest request,
        Guid authorId,
        string searchTrimmed,
        CancellationToken cancellationToken)
    {
        bool hasSearch = !string.IsNullOrEmpty(searchTrimmed);

        // Name-search: имя/username/email живут в AuthService, не в AccessService. Резолвим
        // matching userIds через AuthService, затем фильтруем grantees-ростер по ним. Если
        // search не дал ни одного пользователя — ростер заведомо пуст.
        IReadOnlyList<Guid>? userIdsFilter = null;
        if (hasSearch)
        {
            // Лимит = общий потолок user-search (см. InternalUsersSearchRequest.MAX_LIMIT).
            // Раньше здесь стояло 100 вручную при потолке валидатора 50 → каждый поиск по
            // ростеру падал 500. Держим значение привязанным к контрактной константе.
            Result<IReadOnlyList<AuthUserLookupDto>, Error> searchResult =
                await _authServiceClient.SearchUsersAsync(
                    searchTrimmed, InternalUsersSearchRequest.MAX_LIMIT, cancellationToken);
            if (searchResult.IsFailure)
            {
                // AuthService недоступен → не выдаём пустой ростер под видом «никто не найден»
                // (вводит автора в заблуждение). Пробрасываем ошибку — UI покажет сбой, а не «0».
                return searchResult.Error;
            }

            IReadOnlyList<Guid> matchedIds =
                searchResult.Value.Select(u => u.UserId).Distinct().ToList();
            if (matchedIds.Count == 0)
            {
                return new RenderedStudentsPage([], 0, 0);
            }

            userIdsFilter = matchedIds;
        }

        // Keyset cursor over grants — page N → skip (N-1) keyset pages. GetCourseStudents
        // снаружи всё ещё page/pageSize-based (frontend contract), а grantees отдаёт keyset.
        // Прокручиваем keyset до нужной страницы; для типичных ростеров это 1-2 запроса.
        int targetSkip = (request.Page - 1) * request.PageSize;
        List<CourseGranteeDto> pageGrantees = [];
        int totalCount = 0;
        string? cursor = null;
        int skipped = 0;

        while (true)
        {
            Result<CourseGranteesPage, Error> granteesResult = await _accessServiceClient.GetCourseGranteesAsync(
                request.CourseId,
                authorId,
                userIdsFilter,
                cursor,
                GRANTEES_PAGE_LIMIT,
                cancellationToken);

            if (granteesResult.IsFailure)
            {
                return granteesResult.Error;
            }

            CourseGranteesPage grantees = granteesResult.Value;
            totalCount = grantees.TotalCount;

            foreach (CourseGranteeDto grantee in grantees.Items)
            {
                if (skipped < targetSkip)
                {
                    skipped++;
                    continue;
                }

                if (pageGrantees.Count >= request.PageSize)
                {
                    break;
                }

                pageGrantees.Add(grantee);
            }

            bool pageFull = pageGrantees.Count >= request.PageSize;
            if (pageFull || string.IsNullOrEmpty(grantees.NextCursor))
            {
                break;
            }

            cursor = grantees.NextCursor;
        }

        int totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling((double)totalCount / request.PageSize);

        if (pageGrantees.Count == 0)
        {
            return new RenderedStudentsPage([], totalCount, totalPages);
        }

        Guid[] pageUserIds = pageGrantees.Select(g => g.UserId).Distinct().ToArray();

        // LEFT JOIN локального прогресса по userId — для grant-holder'а без enrollment-строки
        // (новый курс / ещё не начал) поля будут null, тогда используем grant-метаданные.
        Dictionary<Guid, EnrollmentRow> enrollmentByUserId = await LoadEnrollmentsAsync(
            request.CourseId, pageUserIds, cancellationToken);

        Result<IReadOnlyList<AuthUserLookupDto>, Error> usersResult =
            await _authServiceClient.GetUsersByIdsAsync(pageUserIds, cancellationToken);

        IReadOnlyList<AuthUserLookupDto> users = usersResult.IsSuccess ? usersResult.Value : [];
        Dictionary<Guid, AuthUserLookupDto> userById = users.ToDictionary(x => x.UserId, x => x);

        List<CourseStudentDto> items = pageGrantees.Select(grantee =>
        {
            userById.TryGetValue(grantee.UserId, out AuthUserLookupDto? user);
            enrollmentByUserId.TryGetValue(grantee.UserId, out EnrollmentRow? enrollment);

            return new CourseStudentDto(
                enrollment?.Id ?? grantee.GrantId,
                grantee.UserId,
                user?.Name,
                user?.Username,
                user?.Email,
                user?.AvatarId,
                enrollment?.EnrolledAt ?? grantee.GrantedAt.UtcDateTime);
        }).ToList();

        return new RenderedStudentsPage(items, totalCount, totalPages);
    }

    private async Task<Dictionary<Guid, EnrollmentRow>> LoadEnrollmentsAsync(
        Guid courseId,
        Guid[] userIds,
        CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
            SELECT
                ce.id AS Id,
                ce.user_id AS UserId,
                ce.enrolled_at AS EnrolledAt
            FROM course_enrollments ce
            WHERE ce.course_id = @CourseId
              AND ce.user_id = ANY(@UserIds)
            """;

        DynamicParameters parameters = new();
        parameters.Add("CourseId", courseId, DbType.Guid);
        parameters.Add("UserIds", userIds);

        IEnumerable<EnrollmentRow> rows = await connection.QueryAsync<EnrollmentRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        Dictionary<Guid, EnrollmentRow> map = [];
        foreach (EnrollmentRow row in rows)
        {
            map.TryAdd(row.UserId, row);
        }

        return map;
    }

    private sealed record RenderedStudentsPage(
        List<CourseStudentDto> Items,
        int TotalCount,
        int TotalPages);

    private sealed class EnrollmentRow
    {
        public Guid Id { get; init; }
        public Guid UserId { get; init; }
        public DateTime EnrolledAt { get; init; }
    }
}
