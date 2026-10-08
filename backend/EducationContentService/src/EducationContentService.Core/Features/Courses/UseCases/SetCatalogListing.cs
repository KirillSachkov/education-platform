using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.Courses.Queries;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;

namespace EducationContentService.Core.Features.Courses.UseCases;

/// <summary>
///     Admin/moderator approval toggle for catalog visibility (issue #569, model A).
///     Flips <c>Course.IsCatalogListed</c> — display-only, no access-tag / event side effects.
/// </summary>
public sealed record SetCatalogListingCommand(Guid CourseId, bool Listed) : ICommand;

public sealed class SetCatalogListingEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("courses/{courseId:guid}/catalog-listing", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid courseId,
                    [FromBody] SetCatalogListingRequest request,
                    [FromServices] SetCatalogListingHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new SetCatalogListingCommand(courseId, request.Listed), cancellationToken))
            // Витриной курсов управляет модератор/админ контента (bypass'ит Tier-2 ownership),
            // не автор курса — иначе любой автор сам бы опубликовал себя в каталог.
            .RequirePermissions(PlatformPermissions.Content.MODERATE);
    }
}

public sealed class SetCatalogListingHandler : ICommandHandler<Guid, SetCatalogListingCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly HybridCache _cache;
    private readonly ILogger<SetCatalogListingHandler> _logger;

    public SetCatalogListingHandler(
        ICoursesRepository coursesRepository,
        ITransactionManager transactionManager,
        HybridCache cache,
        ILogger<SetCatalogListingHandler> logger)
    {
        _coursesRepository = coursesRepository;
        _transactionManager = transactionManager;
        _cache = cache;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        SetCatalogListingCommand command, CancellationToken cancellationToken)
    {
        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        Course course = courseResult.Value;

        course.SetCatalogListed(command.Listed);

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        // Сбрасываем curriculum/landing кеш курса (как Publish/Update), весь каталог по тегу
        // И портфолио автора по per-author тегу — чтобы одобренный курс появился во всех
        // витринах сразу, а не по истечении 60s TTL.
        await CourseCacheInvalidator.InvalidateAsync(_cache, course.Id, cancellationToken);
        await _cache.RemoveByTagAsync(GetCatalogHandler.CATALOG_CACHE_TAG, cancellationToken);
        await _cache.RemoveByTagAsync(
            $"{GetAuthorCoursesHandler.AuthorCacheTagPrefix}:{course.AuthorId}", cancellationToken);

        _logger.LogInformation(
            "Course {CourseId} catalog listing set to {Listed}", course.Id, command.Listed);

        return course.Id;
    }
}
