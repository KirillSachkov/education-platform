using AccessService.Contracts.PlanGrants.Dtos;
using AccessService.Contracts.PlanGrants.Requests;
using AccessService.Core.Database;
using Core.Abstractions;
using EducationContentService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace AccessService.Core.Features.PlanGrants.UseCases;

/// <summary>
/// Service-to-service endpoint: набор courseId'ов, покрытых активными grant'ами
/// пользователя. COURSE-grant'ы дают courseId напрямую; FULL_ALL / LEARN_ALL
/// раскрываются в полный список курсов платформы через ECS
/// <see cref="IEducationContentServiceClient.GetAllCourseIdsAsync"/>. Legacy FREE
/// раскрывается по автору через <see cref="IEducationContentServiceClient.GetAuthorCourseIdsAsync"/>.
/// Источник "мои курсы" в derive-модели (epic access-derive-model, Phase 0).
/// POST (не GET) — несёт опциональный <c>authorId</c>-фильтр в body, зеркало
/// <c>GetMyCourseProgress?authorId=</c>.
/// </summary>
public sealed class GetUserCoveredCoursesEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/access/users/{userId:guid}/covered-courses", async Task<EndpointResult<CoveredCoursesResult>> (
                [FromRoute] Guid userId,
                [FromBody] CoveredCoursesRequest request,
                [FromServices] GetUserCoveredCoursesHandler handler,
                CancellationToken ct) =>
                await handler.Handle(new GetUserCoveredCoursesQuery(userId, request.AuthorId), ct))
            .RequireAuthorization()
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed record GetUserCoveredCoursesQuery(Guid UserId, Guid? AuthorId) : IQuery;

public sealed class GetUserCoveredCoursesHandler
    : IQueryHandlerWithResult<CoveredCoursesResult, GetUserCoveredCoursesQuery>
{
    private readonly IPlanGrantsRepository _grants;
    private readonly IEducationContentServiceClient _eduClient;
    private readonly ILogger<GetUserCoveredCoursesHandler> _logger;

    public GetUserCoveredCoursesHandler(
        IPlanGrantsRepository grants,
        IEducationContentServiceClient eduClient,
        ILogger<GetUserCoveredCoursesHandler> logger)
    {
        _grants = grants;
        _eduClient = eduClient;
        _logger = logger;
    }

    public async Task<Result<CoveredCoursesResult, Error>> Handle(
        GetUserCoveredCoursesQuery query,
        CancellationToken cancellationToken = default)
    {
        UserGrantScope scope = await _grants.GetUserGrantScopeAsync(query.UserId, cancellationToken);

        HashSet<Guid> covered = [.. scope.ExplicitCourseIds];
        IReadOnlyList<Guid>? filterCourseIds = null;

        if (scope.HasGlobalCourseAccess)
        {
            Result<IReadOnlyList<Guid>, Error> globalCourses;
            if (query.AuthorId is { } globalFilterAuthor)
            {
                globalCourses = await _eduClient.GetAuthorCourseIdsAsync(globalFilterAuthor, cancellationToken);
                if (globalCourses.IsSuccess)
                {
                    filterCourseIds = globalCourses.Value;
                }
            }
            else
            {
                globalCourses = await _eduClient.GetAllCourseIdsAsync(cancellationToken);
            }

            if (globalCourses.IsFailure)
            {
                if (query.AuthorId is { } failedFilterAuthor)
                {
                    _logger.LogWarning(
                        "GetUserCoveredCourses: ECS author-filtered global expansion failed for author {AuthorId}: {ErrorMessage}",
                        failedFilterAuthor,
                        globalCourses.Error.GetMessage());
                    return new CoveredCoursesResult([]);
                }

                // Soft-degrade: ECS down → пропускаем раскрытие global FULL/LEARN.
                // Explicit COURSE id'ы всё ещё можно вернуть.
                _logger.LogWarning(
                    "GetUserCoveredCourses: ECS all-courses expansion failed: {ErrorMessage}",
                    globalCourses.Error.GetMessage());
            }
            else
            {
                foreach (Guid courseId in globalCourses.Value)
                {
                    covered.Add(courseId);
                }
            }
        }

        // FREE-авторы — lower priority (FREE deprecated #358). Включаем их в покрытие
        // курсов "мои курсы" через legacy author expansion.
        IEnumerable<Guid> authorsToExpand = scope.FreeAuthorIds.Distinct();

        foreach (Guid authorId in authorsToExpand)
        {
            Result<IReadOnlyList<Guid>, Error> authorCourses =
                await _eduClient.GetAuthorCourseIdsAsync(authorId, cancellationToken);

            if (authorCourses.IsFailure)
            {
                // Soft-degrade: ECS down → пропускаем раскрытие этого автора. Лучше
                // вернуть частичное покрытие (explicit COURSE id'ы всегда присутствуют),
                // чем уронить "мои курсы" целиком.
                _logger.LogWarning(
                    "GetUserCoveredCourses: ECS author-courses expansion failed for author {AuthorId}: {ErrorMessage}",
                    authorId,
                    authorCourses.Error.GetMessage());
                continue;
            }

            foreach (Guid courseId in authorCourses.Value)
            {
                covered.Add(courseId);
            }
        }

        if (query.AuthorId is { } filterAuthor)
        {
            if (filterCourseIds is null)
            {
                Result<IReadOnlyList<Guid>, Error> filterCourses =
                    await _eduClient.GetAuthorCourseIdsAsync(filterAuthor, cancellationToken);

                if (filterCourses.IsSuccess)
                {
                    filterCourseIds = filterCourses.Value;
                }
                else
                {
                    // Не можем подтвердить принадлежность автору → возвращаем пустой набор,
                    // а не нефильтрованный (не утекаем чужие курсы под видом авторских).
                    _logger.LogWarning(
                        "GetUserCoveredCourses: ECS author-filter resolution failed for author {AuthorId}: {ErrorMessage}",
                        filterAuthor,
                        filterCourses.Error.GetMessage());
                    return new CoveredCoursesResult([]);
                }
            }

            HashSet<Guid> authorCourseSet = [.. filterCourseIds];
            covered.IntersectWith(authorCourseSet);
        }

        return new CoveredCoursesResult([.. covered]);
    }
}
