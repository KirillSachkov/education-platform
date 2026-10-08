using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.CourseQuizzes;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ModuleItems.UseCases;

public sealed record DetachModuleItemCommand(Guid ModuleId, Guid ReferenceId) : ICommand;

public sealed class DetachModuleItemEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("modules/{moduleId:guid}/items/{referenceId:guid}",
            async Task<EndpointResult<Guid>> (
                [FromRoute] Guid moduleId,
                [FromRoute] Guid referenceId,
                [FromServices] DetachModuleItemHandler handler,
                CancellationToken cancellationToken) =>
                await handler.Handle(
                    new DetachModuleItemCommand(moduleId, referenceId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Modules.MANAGE);
    }
}

/// <summary>
///     Открепляет item (Material/Issue/Quiz) от модуля. Удаляется только строка module_items —
///     сам материал/issue/квиз остаётся. Material продолжает существовать в Библиотеке автора и
///     в course_materials, если был привязан к курсу (INV-4 соблюдается — detach из модуля
///     не удаляет course_materials курса, детач от курса делается отдельно через
///     DetachMaterialFromCourse).
///     <para>
///     Quiz-специфика (ST-12 #492): <c>course_quizzes</c> — derived-привязка (нет отдельного
///     attach-to-course flow), поэтому если после detach'а у квиза не осталось module_items
///     в модулях ЭТОГО курса — строка <c>course_quizzes(course, quiz)</c> удаляется и
///     публикуется <c>quiz.access_changed</c> (Redis-теги сужаются).
///     </para>
/// </summary>
public sealed class DetachModuleItemHandler : ICommandHandler<Guid, DetachModuleItemCommand>
{
    private readonly IModuleItemsRepository _moduleItemsRepository;
    private readonly IModulesRepository _modulesRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly ICourseQuizzesRepository _courseQuizzesRepository;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<DetachModuleItemHandler> _logger;

    public DetachModuleItemHandler(
        IModuleItemsRepository moduleItemsRepository,
        IModulesRepository modulesRepository,
        ICoursesRepository coursesRepository,
        ICourseQuizzesRepository courseQuizzesRepository,
        IQuizzesRepository quizzesRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        UserScopedData userScopedData,
        ILogger<DetachModuleItemHandler> logger)
    {
        _moduleItemsRepository = moduleItemsRepository;
        _modulesRepository = modulesRepository;
        _coursesRepository = coursesRepository;
        _courseQuizzesRepository = courseQuizzesRepository;
        _quizzesRepository = quizzesRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        DetachModuleItemCommand command, CancellationToken cancellationToken)
    {
        Result<ModuleItem, Error> itemResult = await _moduleItemsRepository.GetByAsync(
            mi => mi.ModuleId == command.ModuleId && mi.ReferenceId == command.ReferenceId,
            cancellationToken: cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        // SECURITY: только автор курса может открепить item от его модуля.
        // Без этой проверки любой автор мог бы удалять chunks из чужих модулей (IDOR).
        // SECURITY: только автор курса может открепить item от его модуля.
        // Для orphan-модулей (courseId == null) CheckOwnership разрешит только админу.
        Guid? courseId = await _modulesRepository.GetCourseIdAsync(command.ModuleId, cancellationToken);
        Guid? courseAuthorId = null;
        if (courseId.HasValue)
        {
            Result<Domain.Courses.Course, Error> courseResult = await _coursesRepository.GetByAsync(
                c => c.Id == courseId.Value, cancellationToken);
            if (courseResult.IsFailure)
                return courseResult.Error;
            courseAuthorId = courseResult.Value.AuthorId;
        }

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseAuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        ModuleItem item = itemResult.Value;
        _moduleItemsRepository.Delete(item);

        // ST-12 (#492): course_quizzes — derived из размещения в модулях. Если это был
        // последний quiz-item в модулях курса — привязка снимается, теги сужаются.
        if (item.ItemType == ModuleItemType.Quiz && courseId.HasValue)
            await RemoveCourseQuizIfLastItemAsync(item, courseId.Value, cancellationToken);
        else if (item.ItemType == ModuleItemType.Issue)
            await _outbox.PublishAsync(new IssueAccessChanged(item.ReferenceId));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Item {ReferenceId} ({ItemType}) detached from module {ModuleId}",
            command.ReferenceId, item.ItemType, command.ModuleId);

        return item.Id;
    }

    private async Task RemoveCourseQuizIfLastItemAsync(
        ModuleItem item, Guid courseId, CancellationToken cancellationToken)
    {
        bool hasRemaining = await _moduleItemsRepository.HasOtherQuizItemsInCourseAsync(
            courseId, item.ReferenceId, excludedItemId: item.Id, cancellationToken);
        if (hasRemaining)
            return;

        Result<CourseQuiz, Error> bindingResult = await _courseQuizzesRepository.GetByAsync(
            cq => cq.CourseId == courseId && cq.QuizId == item.ReferenceId,
            cancellationToken: cancellationToken);
        if (bindingResult.IsFailure)
            return; // привязки нет (out-of-band состояние) — сужать нечего

        _courseQuizzesRepository.Delete(bindingResult.Value);

        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == item.ReferenceId, cancellationToken);
        if (quizResult.IsFailure)
            return; // defensive: квиз исчез — Redis почистит quiz.hard_deleted

        // GetCourseIdsAsync читает БД (delete ещё не flushed) — удаляемый курс исключаем вручную.
        List<Guid> courseIds = await _courseQuizzesRepository.GetCourseIdsAsync(
            item.ReferenceId, cancellationToken);
        courseIds.Remove(courseId);

        await _outbox.PublishAsync(new QuizAccessChanged(
            item.ReferenceId,
            quizResult.Value.AccessType.ToString(),
            courseIds,
            quizResult.Value.AuthorId));

        _logger.LogInformation(
            "Quiz {QuizId} unbound from course {CourseId} — last module item detached",
            item.ReferenceId, courseId);
    }
}
