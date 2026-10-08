using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.Materials;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Collections;
using EducationContentService.Core.Features.CourseMaterials;
using EducationContentService.Core.Features.Courses;
using EducationContentService.Core.Features.ModuleItems;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.ValueObjects;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Materials.UseCases;

public sealed record CreateDraftMaterialCommand(CreateDraftMaterialRequest Request) : ICommand;

public sealed class CreateDraftMaterialEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("materials/draft", async Task<EndpointResult<Guid>> (
                    [FromBody] CreateDraftMaterialRequest? request,
                    [FromServices] CreateDraftMaterialHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new CreateDraftMaterialCommand(request ?? new CreateDraftMaterialRequest()),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

/// <summary>
///     Создаёт минимальный draft-материал (placeholder title, kind=ARTICLE, нет контента/видео/обложки).
///     Используется YouTube-style flow: фронт открывает форму create → сразу POST /materials/draft →
///     router.replace на /edit/{id}. Все правки идут уже на реальный entity, refresh не теряет
///     прогресс. CourseId/ModuleId опциональны — если есть, материал сразу прикрепляется к
///     курсу/модулю в той же транзакции (как в обычном Create). CollectionId+SectionId опциональны —
///     если оба заданы, материал атомарно добавляется в подборку в той же транзакции (issue #216).
/// </summary>
public sealed class CreateDraftMaterialHandler : ICommandHandler<Guid, CreateDraftMaterialCommand>
{
    private const string PLACEHOLDER_TITLE = "Без названия";

    private readonly IMaterialsRepository _materialsRepository;
    private readonly ICoursesRepository _coursesRepository;
    private readonly IModulesRepository _modulesRepository;
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICollectionSectionsRepository _sectionsRepository;
    private readonly ICollectionItemsRepository _collectionItemsRepository;
    private readonly CourseMaterialService _courseMaterialService;
    private readonly ModuleItemService _moduleItemService;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly ILogger<CreateDraftMaterialHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public CreateDraftMaterialHandler(
        IMaterialsRepository materialsRepository,
        ICoursesRepository coursesRepository,
        IModulesRepository modulesRepository,
        ICollectionsRepository collectionsRepository,
        ICollectionSectionsRepository sectionsRepository,
        ICollectionItemsRepository collectionItemsRepository,
        CourseMaterialService courseMaterialService,
        ModuleItemService moduleItemService,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        ILogger<CreateDraftMaterialHandler> logger,
        UserScopedData userScopedData)
    {
        _materialsRepository = materialsRepository;
        _coursesRepository = coursesRepository;
        _modulesRepository = modulesRepository;
        _collectionsRepository = collectionsRepository;
        _sectionsRepository = sectionsRepository;
        _collectionItemsRepository = collectionItemsRepository;
        _courseMaterialService = courseMaterialService;
        _moduleItemService = moduleItemService;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        CreateDraftMaterialCommand command,
        CancellationToken cancellationToken)
    {
        // CollectionId и SectionId идут парой: либо оба null, либо оба заданы.
        // Половинчатый payload — пользовательская ошибка, валидируем рано.
        bool hasCollection = command.Request.CollectionId is not null;
        bool hasSection = command.Request.SectionId is not null;
        if (hasCollection != hasSection)
            return GeneralErrors.ValueIsInvalid("CollectionId+SectionId");

        // Resolve courseId: explicitly passed, or derived from module.
        Guid? courseId = command.Request.CourseId;
        if (command.Request.ModuleId is { } moduleId)
        {
            Guid? moduleCourseId = await _modulesRepository.GetCourseIdAsync(moduleId, cancellationToken);
            if (moduleCourseId is null)
                return EducationErrors.ModuleNotAttachedToCourse(moduleId);

            if (courseId is null)
                courseId = moduleCourseId;
            else if (courseId != moduleCourseId)
                return EducationErrors.ModuleCourseMismatch(moduleId, courseId.Value);
        }

        // Course ownership check — author can only attach to their own courses.
        if (courseId is { } cid)
        {
            Result<Domain.Courses.Course, Error> courseResult =
                await _coursesRepository.GetByAsync(c => c.Id == cid, cancellationToken);
            if (courseResult.IsFailure)
                return courseResult.Error;

            UnitResult<Error> ownership = _userScopedData.CheckOwnership(courseResult.Value.AuthorId);
            if (ownership.IsFailure)
                return ownership.Error;
        }

        // Pre-resolve коллекцию и секцию ДО создания материала — если автор передал
        // чужую/несуществующую подборку, разворачиваемся без побочных эффектов.
        Collection? collection = null;
        CollectionSection? section = null;
        if (command.Request.CollectionId is { } colId && command.Request.SectionId is { } secId)
        {
            Result<Collection, Error> collectionResult = await _collectionsRepository.GetByAsync(
                c => c.Id == colId, cancellationToken);
            if (collectionResult.IsFailure)
                return collectionResult.Error;

            UnitResult<Error> collectionOwnership = _userScopedData.CheckOwnership(collectionResult.Value.AuthorId);
            if (collectionOwnership.IsFailure)
                return collectionOwnership.Error;

            Result<CollectionSection, Error> sectionResult = await _sectionsRepository.GetByAsync(
                s => s.Id == secId && s.CollectionId == colId,
                cancellationToken: cancellationToken);
            if (sectionResult.IsFailure)
                return EducationErrors.ItemNotFound("CollectionSection", secId);

            collection = collectionResult.Value;
            section = sectionResult.Value;
        }

        // Default AccessType: ENROLLED для course-bound, PUBLIC для orphan — зеркалит UI-default
        // обычного create. Юзер потом меняет через UpdateMaterialAccess.
        AccessType accessType = courseId is not null ? AccessType.ENROLLED : AccessType.PUBLIC;

        Result<Title, Error> titleResult = Title.Create(PLACEHOLDER_TITLE);
        if (titleResult.IsFailure)
            return titleResult.Error;

        Material material = new(_userScopedData.UserId, titleResult.Value, MaterialKind.ARTICLE, accessType);

        await _materialsRepository.AddAsync(material, cancellationToken);

        // Optional: attach to course (course_materials).
        if (courseId is { } attachCourseId)
        {
            Result<Domain.Courses.CourseMaterial, Error> cmResult = await _courseMaterialService.CreateAsync(
                attachCourseId, material.Id, cancellationToken);
            if (cmResult.IsFailure)
                return cmResult.Error;
        }

        // Optional: attach to module (module_items).
        if (command.Request.ModuleId is { } attachModuleId)
        {
            Result<ModuleItem, Error> miResult = await _moduleItemService.CreateAsync(
                attachModuleId, ModuleItemType.Material, material.Id, cancellationToken,
                ViewPriority.Key);
            if (miResult.IsFailure)
                return miResult.Error;
        }

        // Optional: attach to collection section (collection_items). SortKey ставим в конец
        // секции — повторяет логику AddItemHandler. Дубликат-чек не нужен: материал только
        // что создан, физически не может быть в этой или любой другой секции.
        if (collection is not null && section is not null)
        {
            Result<CollectionItem, Error> lastResult = await _collectionItemsRepository.GetByAsync(
                i => i.SectionId == section.Id,
                orderBy: i => i.SortKey,
                descending: true,
                cancellationToken);

            SortKey sortKey = lastResult.IsSuccess
                ? SortKey.After(lastResult.Value.SortKey)
                : SortKey.Initial();

            CollectionItem item = new(section.Id, CollectionItemType.MATERIAL, material.Id, sortKey);
            await _collectionItemsRepository.AddAsync(item, cancellationToken);
            collection.Touch();
        }

        // Publish MaterialCreated так же как обычный create — content-access sync handler
        // выставит Redis-теги (для DRAFT они закрытые, что норма).
        await _outbox.PublishAsync(new MaterialCreated(
            material.Id, material.AccessType.ToString(), material.AuthorId));

        if (courseId is { } accessCourseId)
        {
            await _outbox.PublishAsync(new MaterialAccessChanged(
                material.Id,
                material.AccessType.ToString(),
                new List<Guid> { accessCourseId },
                material.AuthorId));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Draft material {MaterialId} created by {AuthorId} (CourseId={CourseId}, ModuleId={ModuleId}, CollectionId={CollectionId}, SectionId={SectionId})",
            material.Id, material.AuthorId, courseId, command.Request.ModuleId,
            command.Request.CollectionId, command.Request.SectionId);

        return material.Id;
    }
}
