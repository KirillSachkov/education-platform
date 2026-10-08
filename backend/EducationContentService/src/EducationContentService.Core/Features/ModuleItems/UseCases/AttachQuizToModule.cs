using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.CourseQuizzes;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Quizzes;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ModuleItems.UseCases;

public sealed record AttachQuizToModuleCommand(Guid ModuleId, Guid QuizId) : ICommand;

public sealed record AttachQuizToModuleRequest(Guid QuizId);

public sealed class AttachQuizToModuleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("modules/{moduleId:guid}/quizzes", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid moduleId,
                    [FromBody] AttachQuizToModuleRequest request,
                    [FromServices] AttachQuizToModuleHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new AttachQuizToModuleCommand(moduleId, request.QuizId),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Привязывает квиз к модулю (ST-12 #492, зеркало <see cref="AttachMaterialToModuleHandler"/>).
///     Поддерживает INV-4-зеркало: <c>course_quizzes</c> — derived-привязка «квиз размещён хотя бы
///     в одном модуле курса». Если записи (course, quiz) ещё нет — она создаётся автоматически
///     в той же транзакции (<c>AddIfMissingAsync</c>) и публикуется <c>quiz.access_changed</c>,
///     чтобы Redis-теги ENROLLED-квиза пересчитались с учётом нового курса.
///     DRAFT-квиз привязывать можно (как DRAFT-материал) — в студенческой программе он скрыт
///     PUBLISHED-фильтром, автор видит его в билдере.
/// </summary>
public sealed class AttachQuizToModuleHandler : ICommandHandler<Guid, AttachQuizToModuleCommand>
{
    private readonly IModulesRepository _modulesRepository;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly ICourseQuizzesRepository _courseQuizzesRepository;
    private readonly ModuleItemService _moduleItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<AttachQuizToModuleHandler> _logger;

    public AttachQuizToModuleHandler(
        IModulesRepository modulesRepository,
        IQuizzesRepository quizzesRepository,
        ICoursesRepository coursesRepository,
        ICourseQuizzesRepository courseQuizzesRepository,
        ModuleItemService moduleItemService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        UserScopedData userScopedData,
        ILogger<AttachQuizToModuleHandler> logger)
    {
        _modulesRepository = modulesRepository;
        _quizzesRepository = quizzesRepository;
        _coursesRepository = coursesRepository;
        _courseQuizzesRepository = courseQuizzesRepository;
        _moduleItemService = moduleItemService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        AttachQuizToModuleCommand command, CancellationToken cancellationToken)
    {
        Result<Module, Error> moduleResult = await _modulesRepository.GetByAsync(
            m => m.Id == command.ModuleId, cancellationToken);
        if (moduleResult.IsFailure)
            return moduleResult.Error;

        Result<Quiz, Error> quizResult = await _quizzesRepository.GetByAsync(
            q => q.Id == command.QuizId, cancellationToken);
        if (quizResult.IsFailure)
            return EducationErrors.QuizNotFound(command.QuizId);

        Quiz quiz = quizResult.Value;

        // SECURITY: автор может разместить только СВОЙ квиз. Иначе IDOR — другой автор
        // мог бы вставить чужой квиз в свой курс и менять Redis-теги чужого ресурса.
        UnitResult<Error> quizOwnership = _userScopedData.CheckOwnership(quiz.AuthorId);
        if (quizOwnership.IsFailure)
            return quizOwnership.Error;

        // INV-4-зеркало: course_quizzes должна существовать для курса этого модуля.
        Guid? courseId = await _modulesRepository.GetCourseIdAsync(command.ModuleId, cancellationToken);
        if (courseId is null)
            return EducationErrors.ModuleNotAttachedToCourse(command.ModuleId);

        // SECURITY: автор может расширять только СВОЙ курс. Защищает от инъекции
        // квиза в чужой курс через привязку к чужому модулю.
        Result<Domain.Courses.Course, Error> courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == courseId.Value, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        UnitResult<Error> courseOwnership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
        if (courseOwnership.IsFailure)
            return courseOwnership.Error;

        bool accessChanged = await _courseQuizzesRepository.AddIfMissingAsync(
            courseId.Value, command.QuizId, cancellationToken);

        Result<ModuleItem, Error> itemResult = await _moduleItemService.CreateAsync(
            command.ModuleId, ModuleItemType.Quiz, command.QuizId, cancellationToken);
        if (itemResult.IsFailure)
            return itemResult.Error;

        // Если auto-создали course_quizzes — Redis-теги квиза должны пересчитаться,
        // чтобы ENROLLED-квиз стал виден plan-студентам этого курса.
        if (accessChanged)
        {
            List<Guid> courseIds = await _courseQuizzesRepository.GetCourseIdsAsync(
                command.QuizId, cancellationToken);
            if (!courseIds.Contains(courseId.Value))
                courseIds.Add(courseId.Value);

            await _outbox.PublishAsync(new QuizAccessChanged(
                command.QuizId,
                quiz.AccessType.ToString(),
                courseIds,
                quiz.AuthorId));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // Новый item появился в curriculum/landing курса — сбрасываем кэш,
        // чтобы автор немедленно увидел его в сайдбаре.
        // Недоступность Redis не откатывает уже зафиксированную транзакцию.
        try
        {
            await CourseCacheInvalidator.InvalidateAsync(_cache, courseId.Value, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Curriculum cache invalidation failed after attaching quiz {QuizId} to module {ModuleId} (will expire via TTL)",
                command.QuizId, command.ModuleId);
        }

        _logger.LogInformation(
            "Quiz {QuizId} bound to module {ModuleId} (auto-created course_quizzes: {AutoCreated})",
            command.QuizId, command.ModuleId, accessChanged);

        return itemResult.Value.Id;
    }
}
