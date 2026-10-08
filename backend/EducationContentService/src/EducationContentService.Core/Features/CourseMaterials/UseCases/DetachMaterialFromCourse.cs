using System.Data.Common;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.Materials;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.CourseMaterials.UseCases;

public sealed record DetachMaterialFromCourseCommand(Guid CourseId, Guid MaterialId) : ICommand;

public sealed record DetachMaterialFromCourseResponse(Guid MaterialId);

public sealed class DetachMaterialFromCourseEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("courses/{courseId:guid}/materials/{materialId:guid}",
                async Task<EndpointResult<DetachMaterialFromCourseResponse>> (
                    [FromRoute] Guid courseId,
                    [FromRoute] Guid materialId,
                    [FromServices] DetachMaterialFromCourseHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new DetachMaterialFromCourseCommand(courseId, materialId),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Открепляет материал от курса.
///     Поддерживает инварианты INV-4 (каскадное удаление module_items материала в модулях курса)
///     и INV-10 (auto-downgrade AccessType до PUBLIC, если материал больше не привязан ни к одному курсу).
///     См. MATERIAL_LIFECYCLE.md сценарий 5.
/// </summary>
public sealed class DetachMaterialFromCourseHandler
    : ICommandHandler<DetachMaterialFromCourseResponse, DetachMaterialFromCourseCommand>
{
    private readonly ICoursesRepository _coursesRepository;
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ICourseMaterialsRepository _courseMaterialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly ILogger<DetachMaterialFromCourseHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public DetachMaterialFromCourseHandler(
        ICoursesRepository coursesRepository,
        IMaterialsRepository materialsRepository,
        ICourseMaterialsRepository courseMaterialsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        ILogger<DetachMaterialFromCourseHandler> logger,
        UserScopedData userScopedData)
    {
        _coursesRepository = coursesRepository;
        _materialsRepository = materialsRepository;
        _courseMaterialsRepository = courseMaterialsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<DetachMaterialFromCourseResponse, Error>> Handle(
        DetachMaterialFromCourseCommand command,
        CancellationToken cancellationToken)
    {
        var courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == command.CourseId, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        var courseMaterialResult = await _courseMaterialsRepository.GetByAsync(
            cm => cm.CourseId == command.CourseId && cm.MaterialId == command.MaterialId,
            cancellationToken: cancellationToken);
        if (courseMaterialResult.IsFailure)
            return courseMaterialResult.Error;

        _courseMaterialsRepository.Delete(courseMaterialResult.Value);

        // INV-4: cascade delete module_items of this material in modules of this course.
        // Modules are linked to a course via course_items.
        DbConnection connection = _transactionManager.GetDbConnection();
        const string deleteModuleItemsSql = """
            DELETE FROM module_items
            WHERE item_type = 'Material'
              AND reference_id = @MaterialId
              AND module_id IN (
                  SELECT ci.reference_id
                  FROM course_items ci
                  WHERE ci.course_id = @CourseId AND ci.item_type = 'Module'
              );
            """;
        int deletedModuleItems = await connection.ExecuteAsync(
            new CommandDefinition(
                deleteModuleItemsSql,
                new { command.MaterialId, command.CourseId },
                cancellationToken: cancellationToken));

        // Публикуем MaterialAccessChanged по новому списку courseIds — sync handler
        // пересчитает Redis-теги. Список может стать пустым → orphan, платный доступ
        // через платформенный plan:all (см. ContentAccessTagBuilder, #77).
        var materialResult = await _materialsRepository.GetByAsync(
            m => m.Id == command.MaterialId, cancellationToken);
        if (materialResult.IsSuccess)
        {
            // DB ещё содержит старую course_materials строку — вычитаем её вручную.
            List<Guid> remainingCourseIds = await _materialsRepository.GetCourseIdsAsync(
                command.MaterialId, cancellationToken);
            remainingCourseIds.Remove(command.CourseId);

            await _outbox.PublishAsync(new MaterialAccessChanged(
                command.MaterialId,
                materialResult.Value.AccessType.ToString(),
                remainingCourseIds,
                materialResult.Value.AuthorId));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        // Material исчез из curriculum/landing этого курса — сбрасываем кэш,
        // иначе автор/студенты видят его в сайдбаре до 3 минут после detach.
        // Недоступность Redis не откатывает уже зафиксированную транзакцию.
        try
        {
            await CourseCacheInvalidator.InvalidateAsync(_cache, command.CourseId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Curriculum cache invalidation failed after detaching material {MaterialId} from course {CourseId} (will expire via TTL)",
                command.MaterialId, command.CourseId);
        }

        _logger.LogInformation(
            "Material {MaterialId} detached from course {CourseId} (cascade removed {ModuleItemCount} module_items)",
            command.MaterialId, command.CourseId, deletedModuleItems);

        return new DetachMaterialFromCourseResponse(command.MaterialId);
    }
}
