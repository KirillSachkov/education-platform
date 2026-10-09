using ContentAccess;
using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Collections;
using EducationContentService.Core.Features.CourseItems;
using EducationContentService.Core.Features.CourseMaterials;
using EducationContentService.Core.Features.CourseQuizzes;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Courses.UseCases;

public sealed record DeleteCourseCommand(Guid CourseId) : ICommand;

public sealed class DeleteCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("courses/{courseId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid courseId,
                    [FromServices] DeleteCourseHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new DeleteCourseCommand(courseId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Courses.MANAGE);
    }
}

public sealed class DeleteCourseHandler : ICommandHandler<Guid, DeleteCourseCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICourseItemsRepository _courseItemsRepository;
    private readonly ICourseMaterialsRepository _courseMaterialsRepository;
    private readonly ICourseQuizzesRepository _courseQuizzesRepository;
    private readonly IMaterialsRepository _materialsRepository;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly IIssuesRepository _issuesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IResourceAccessWriter _resourceAccessWriter;
    private readonly IUserGrantWriter _userGrantWriter;
    private readonly HybridCache _cache;
    private readonly ILogger<DeleteCourseHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public DeleteCourseHandler(
        ICoursesRepository coursesRepository,
        ICollectionsRepository collectionsRepository,
        ICourseItemsRepository courseItemsRepository,
        ICourseMaterialsRepository courseMaterialsRepository,
        ICourseQuizzesRepository courseQuizzesRepository,
        IMaterialsRepository materialsRepository,
        IQuizzesRepository quizzesRepository,
        IIssuesRepository issuesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IResourceAccessWriter resourceAccessWriter,
        IUserGrantWriter userGrantWriter,
        HybridCache cache,
        ILogger<DeleteCourseHandler> logger,
        UserScopedData userScopedData)
    {
        _coursesRepository = coursesRepository;
        _collectionsRepository = collectionsRepository;
        _courseItemsRepository = courseItemsRepository;
        _courseMaterialsRepository = courseMaterialsRepository;
        _courseQuizzesRepository = courseQuizzesRepository;
        _materialsRepository = materialsRepository;
        _quizzesRepository = quizzesRepository;
        _issuesRepository = issuesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _resourceAccessWriter = resourceAccessWriter;
        _userGrantWriter = userGrantWriter;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(DeleteCourseCommand command, CancellationToken cancellationToken)
    {
        Result<Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        Course course = courseResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(course.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        UnitResult<Error> begin = await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (begin.IsFailure)
            return begin.Error;

        IReadOnlyList<Guid> affectedIssueIds = await _issuesRepository.GetIdsByCourseIdAsync(
            course.Id,
            cancellationToken);
        foreach (Guid issueId in affectedIssueIds)
            await _outbox.PublishAsync(new IssueAccessChanged(issueId));

        List<CourseItem> courseItems = await _courseItemsRepository.GetManyByAsync(
            item => item.CourseId == course.Id,
            cancellationToken);
        foreach (CourseItem courseItem in courseItems)
            _courseItemsRepository.Delete(courseItem);

        List<CourseMaterial> courseMaterials = await _courseMaterialsRepository.GetManyByAsync(
            material => material.CourseId == course.Id,
            cancellationToken);
        foreach (CourseMaterial courseMaterial in courseMaterials)
            _courseMaterialsRepository.Delete(courseMaterial);

        foreach (Guid materialId in courseMaterials.Select(link => link.MaterialId).Distinct())
        {
            Result<Material, Error> materialResult = await _materialsRepository.GetByAsync(
                material => material.Id == materialId,
                cancellationToken);
            if (materialResult.IsFailure)
                continue;

            List<Guid> remainingCourseIds = await _materialsRepository.GetCourseIdsAsync(
                materialId,
                cancellationToken);
            remainingCourseIds.Remove(course.Id);
            await _outbox.PublishAsync(new MaterialAccessChanged(
                materialId,
                materialResult.Value.AccessType.ToString(),
                remainingCourseIds,
                materialResult.Value.AuthorId));
        }

        // До удаления курса публикуем CollectionAccessChanged для всех его подборок:
        // после SetNull у FK collection.course_id Redis-теги пересчитаются с учётом orphan-статуса
        // (через платформенный plan:all для ENROLLED, см. ContentAccessTagBuilder + #77).
        Guid courseId = course.Id;
        List<Collection> affectedCollections =
            await _collectionsRepository.GetManyByAsync(c => c.CourseId == courseId, cancellationToken);
        foreach (Collection collection in affectedCollections)
        {
            collection.OnCourseDetached();
            await _outbox.PublishAsync(new CollectionAccessChanged(
                collection.Id,
                collection.AccessType.ToString(),
                collection.CourseId,
                collection.AuthorId));
        }

        // course_quizzes — derived-привязки (#492): без явного cleanup'а строки удалённого
        // курса остаются навсегда, и resync/sync-handler'ы продолжают тегировать квизы
        // несуществующим курсом (stale-доступ). Сносим и публикуем QuizAccessChanged по
        // каждому затронутому квизу — зеркало OnCourseDetached у подборок выше.
        List<CourseQuiz> courseQuizLinks =
            await _courseQuizzesRepository.GetManyByAsync(cq => cq.CourseId == courseId, cancellationToken);
        if (courseQuizLinks.Count > 0)
        {
            foreach (CourseQuiz link in courseQuizLinks)
            {
                _courseQuizzesRepository.Delete(link);

                Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
                    q => q.Id == link.QuizId, cancellationToken);
                if (quizResult.IsFailure)
                    continue;

                Quiz quiz = quizResult.Value;
                List<Guid> remainingCourseIds =
                    await _courseQuizzesRepository.GetCourseIdsAsync(quiz.Id, cancellationToken);
                remainingCourseIds.Remove(courseId);
                await _outbox.PublishAsync(new QuizAccessChanged(
                    quiz.Id,
                    quiz.AccessType.ToString(),
                    remainingCourseIds,
                    quiz.AuthorId));
            }
        }


        await _coursesRepository.DeleteAsync(course, cancellationToken);

        await _outbox.PublishAsync(new CourseHardDeleted(course.Id));

        UnitResult<Error> result = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (result.IsFailure)
            return result.Error;

        UnitResult<Error> commit = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commit.IsFailure)
            return commit.Error;

        await _resourceAccessWriter.ClearTagsAsync(ResourceTypes.COURSE, course.Id, cancellationToken);
        await _userGrantWriter.RevokeAsync(course.AuthorId, GrantTags.Course(course.Id), cancellationToken);

        await CourseCacheInvalidator.InvalidateAsync(_cache, course.Id, cancellationToken);

        _logger.LogInformation("Course {CourseId} deleted", course.Id);

        return course.Id;
    }
}