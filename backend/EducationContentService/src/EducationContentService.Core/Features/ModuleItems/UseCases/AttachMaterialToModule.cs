using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.CourseMaterials;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ModuleItems.UseCases;

public sealed record AttachMaterialToModuleCommand(Guid ModuleId, Guid MaterialId) : ICommand;

public sealed record AttachMaterialToModuleRequest(Guid MaterialId);

public sealed class AttachMaterialToModuleEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("modules/{moduleId:guid}/materials", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid moduleId,
                    [FromBody] AttachMaterialToModuleRequest request,
                    [FromServices] AttachMaterialToModuleHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new AttachMaterialToModuleCommand(moduleId, request.MaterialId),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Modules.MANAGE);
    }
}

/// <summary>
///     Привязывает материал к модулю. Поддерживает инвариант INV-4 из MATERIAL_LIFECYCLE.md:
///     если <c>course_materials</c> записи для этого материала в курсе модуля не существует — создаётся автоматически
///     в той же транзакции. Это позволяет фронту одним запросом «перетащить материал в модуль» без предварительного
///     attach к курсу.
/// </summary>
public sealed class AttachMaterialToModuleHandler : ICommandHandler<Guid, AttachMaterialToModuleCommand>
{
    private readonly IModulesRepository _modulesRepository;
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly ICourseMaterialsRepository _courseMaterialsRepository;
    private readonly CourseMaterialService _courseMaterialService;
    private readonly ModuleItemService _moduleItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly UserScopedData _userScopedData;
    private readonly ILogger<AttachMaterialToModuleHandler> _logger;

    public AttachMaterialToModuleHandler(
        IModulesRepository modulesRepository,
        IMaterialsRepository materialsRepository,
        ICoursesRepository coursesRepository,
        ICourseMaterialsRepository courseMaterialsRepository,
        CourseMaterialService courseMaterialService,
        ModuleItemService moduleItemService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        UserScopedData userScopedData,
        ILogger<AttachMaterialToModuleHandler> logger)
    {
        _modulesRepository = modulesRepository;
        _materialsRepository = materialsRepository;
        _coursesRepository = coursesRepository;
        _courseMaterialsRepository = courseMaterialsRepository;
        _courseMaterialService = courseMaterialService;
        _moduleItemService = moduleItemService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _userScopedData = userScopedData;
        _logger = logger;
    }

    public async Task<Result<Guid, Error>> Handle(
        AttachMaterialToModuleCommand command, CancellationToken cancellationToken)
    {
        Result<Module, Error> moduleResult = await _modulesRepository.GetByAsync(
            m => m.Id == command.ModuleId, cancellationToken);
        if (moduleResult.IsFailure)
            return moduleResult.Error;

        Result<Material, Error> materialResult = await _materialsRepository.GetByAsync(
            m => m.Id == command.MaterialId, cancellationToken);
        if (materialResult.IsFailure)
            return materialResult.Error;

        // SECURITY: автор может прикрепить только СВОЙ материал. Иначе IDOR — другой автор
        // мог бы вставить чужой материал в свой курс или менять Redis-теги чужого ресурса.
        UnitResult<Error> materialOwnership = _userScopedData.CheckOwnership(materialResult.Value.AuthorId);
        if (materialOwnership.IsFailure)
            return materialOwnership.Error;

        // INV-4: ensure course_materials exists for the course this module belongs to.
        Guid? courseId = await _modulesRepository.GetCourseIdAsync(command.ModuleId, cancellationToken);
        if (courseId is null)
            return EducationErrors.ModuleNotAttachedToCourse(command.ModuleId);

        // SECURITY: автор может расширять только СВОЙ курс. Защищает от инъекции
        // материала в чужой курс через привязку к чужому модулю.
        var courseResult = await _coursesRepository.GetByAsync(
            c => c.Id == courseId.Value, cancellationToken);
        if (courseResult.IsFailure)
            return courseResult.Error;

        UnitResult<Error> courseOwnership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
        if (courseOwnership.IsFailure)
            return courseOwnership.Error;

        bool accessChanged = false;
        Result<Domain.Courses.CourseMaterial, Error> existingCourseMaterial = await _courseMaterialsRepository.GetByAsync(
            cm => cm.CourseId == courseId.Value && cm.MaterialId == command.MaterialId,
            cancellationToken: cancellationToken);

        if (existingCourseMaterial.IsFailure)
        {
            // Auto-create CourseMaterial — material becomes course-level.
            Result<Domain.Courses.CourseMaterial, Error> cmResult = await _courseMaterialService.CreateAsync(
                courseId.Value, command.MaterialId, cancellationToken);
            if (cmResult.IsFailure)
                return cmResult.Error;

            accessChanged = true;
        }

        // Default ViewPriority=Key для материалов — основной материал модуля.
        Result<ModuleItem, Error> itemResult = await _moduleItemService.CreateAsync(
            command.ModuleId, ModuleItemType.Material, command.MaterialId, cancellationToken,
            ViewPriority.Key);
        if (itemResult.IsFailure)
            return itemResult.Error;

        // If we auto-created a CourseMaterial, Redis tags must be re-synced so the material
        // becomes visible to enrolled students of this course.
        if (accessChanged)
        {
            List<Guid> courseIds = await _materialsRepository.GetCourseIdsAsync(command.MaterialId, cancellationToken);
            if (!courseIds.Contains(courseId.Value))
                courseIds.Add(courseId.Value);

            await _outbox.PublishAsync(new MaterialAccessChanged(
                command.MaterialId,
                materialResult.Value.AccessType.ToString(),
                courseIds,
                materialResult.Value.AuthorId));
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
                "Curriculum cache invalidation failed after attaching material {MaterialId} to module {ModuleId} (will expire via TTL)",
                command.MaterialId, command.ModuleId);
        }

        _logger.LogInformation(
            "Material {MaterialId} bound to module {ModuleId} (auto-created course_materials: {AutoCreated})",
            command.MaterialId, command.ModuleId, accessChanged);

        return itemResult.Value.Id;
    }
}
