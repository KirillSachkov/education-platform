using Core.Abstractions;
using Core.Database;
using Core.Validation;
using EducationContentService.Contracts.Collections;
using EducationContentService.Core.Database;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Materials;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Collections.UseCases;

public sealed record BulkSetCollectionItemsAccessTypeCommand(
    Guid CollectionId,
    BulkSetItemsAccessTypeRequest Request) : ICommand;

public sealed class BulkSetItemsAccessTypeRequestValidator : AbstractValidator<BulkSetItemsAccessTypeRequest>
{
    public BulkSetItemsAccessTypeRequestValidator()
    {
        RuleFor(x => x.AccessType)
            .NotEmpty()
            .WithError(EducationErrors.InvalidAccessType())
            .Must(value => Enum.TryParse<AccessType>(value, ignoreCase: true, out _))
            .WithError(EducationErrors.InvalidAccessType());
    }
}

public sealed class BulkSetCollectionItemsAccessTypeEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPatch("collections/{collectionId:guid}/items/access-type",
                async Task<EndpointResult<BulkSetItemsAccessTypeResponse>> (
                    [FromRoute] Guid collectionId,
                    [FromBody] BulkSetItemsAccessTypeRequest request,
                    [FromServices] BulkSetCollectionItemsAccessTypeHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new BulkSetCollectionItemsAccessTypeCommand(collectionId, request),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class BulkSetCollectionItemsAccessTypeHandler
    : ICommandHandler<BulkSetItemsAccessTypeResponse, BulkSetCollectionItemsAccessTypeCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICollectionSectionsRepository _sectionsRepository;
    private readonly ICollectionItemsRepository _itemsRepository;
    private readonly IMaterialsRepository _materialsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<BulkSetItemsAccessTypeRequest> _validator;
    private readonly ILogger<BulkSetCollectionItemsAccessTypeHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public BulkSetCollectionItemsAccessTypeHandler(
        ICollectionsRepository collectionsRepository,
        ICollectionSectionsRepository sectionsRepository,
        ICollectionItemsRepository itemsRepository,
        IMaterialsRepository materialsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<BulkSetItemsAccessTypeRequest> validator,
        ILogger<BulkSetCollectionItemsAccessTypeHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _sectionsRepository = sectionsRepository;
        _itemsRepository = itemsRepository;
        _materialsRepository = materialsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<BulkSetItemsAccessTypeResponse, Error>> Handle(
        BulkSetCollectionItemsAccessTypeCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<Collection, Error> collectionResult = await _collectionsRepository.GetByAsync(
            c => c.Id == command.CollectionId, cancellationToken);
        if (collectionResult.IsFailure)
            return collectionResult.Error;

        Collection collection = collectionResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(collection.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        var newAccessType = Enum.Parse<AccessType>(command.Request.AccessType, ignoreCase: true);

        // 1. Все секции подборки.
        List<CollectionSection> sections = await _sectionsRepository.GetManyByAsync(
            s => s.CollectionId == collection.Id, cancellationToken);

        if (sections.Count == 0)
            return new BulkSetItemsAccessTypeResponse(0, 0, 0, 0);

        Guid[] sectionIds = sections.Select(s => s.Id).ToArray();

        // 2. Все items во всех секциях — рекурсивно (top-level secs + nested items).
        // Только MATERIAL-элементы (#491): у квизов собственный AccessType на самом
        // квизе — bulk-операция подборки его сознательно не трогает.
        List<CollectionItem> items = await _itemsRepository.GetManyByAsync(
            i => sectionIds.Contains(i.SectionId) && i.ItemType == CollectionItemType.MATERIAL,
            cancellationToken);

        // Уникальные materialIds — один материал может быть в нескольких секциях
        // подборки; идемпотентность важна, чтобы не публиковать event дважды.
        Guid[] materialIds = items.Select(i => i.ReferenceId).Distinct().ToArray();

        if (materialIds.Length == 0)
            return new BulkSetItemsAccessTypeResponse(0, 0, 0, 0);

        IReadOnlyList<Material> materials = await _materialsRepository.GetManyByAsync(
            m => materialIds.Contains(m.Id), cancellationToken);

        // Батч-проекция courseIds → используется в payload'е MaterialAccessChanged
        // (нужно для пересчёта Redis-тегов).
        IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> courseIdsByMaterial =
            await _materialsRepository.GetCourseIdsBatchAsync(materialIds, cancellationToken);

        int updated = 0;
        int skipped = 0;
        int skippedNotOwned = 0;

        foreach (Material material in materials)
        {
            // Не-админ имеет право мутировать только свои материалы. Если в чужой подборке
            // лежит материал стороннего автора — пропускаем без ошибки (учитываем в счётчике).
            if (!_userScopedData.IsAdmin && material.AuthorId != _userScopedData.UserId)
            {
                skippedNotOwned++;
                continue;
            }

            bool changed = material.ChangeAccessType(newAccessType);
            if (!changed)
            {
                skipped++;
                continue;
            }

            IReadOnlyList<Guid> courseIds = courseIdsByMaterial.TryGetValue(material.Id, out IReadOnlyList<Guid>? cids)
                ? cids
                : Array.Empty<Guid>();

            await _outbox.PublishAsync(new MaterialAccessChanged(
                material.Id,
                material.AccessType.ToString(),
                courseIds,
                material.AuthorId));

            updated++;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Bulk access-type change in collection {CollectionId} → {AccessType}: " +
            "updated={Updated}, skipped={Skipped}, skippedNotOwned={SkippedNotOwned}, total={Total}",
            collection.Id, newAccessType, updated, skipped, skippedNotOwned, materialIds.Length);

        return new BulkSetItemsAccessTypeResponse(updated, skipped, skippedNotOwned, materialIds.Length);
    }
}
