using Core.Abstractions;
using Core.Database;
using EducationContentService.Contracts.Collections;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Ordering;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.UseCases;

public sealed record AddItemCommand(
    Guid CollectionId,
    Guid SectionId,
    AddItemRequest Request) : ICommand;

public sealed class AddItemEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("collections/{collectionId:guid}/sections/{sectionId:guid}/items",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromRoute] Guid sectionId,
                    [FromBody] AddItemRequest request,
                    [FromServices] AddItemHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new AddItemCommand(collectionId, sectionId, request),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class AddItemHandler : ICommandHandler<Guid, AddItemCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICollectionSectionsRepository _sectionsRepository;
    private readonly ICollectionItemsRepository _itemsRepository;
    private readonly IMaterialsRepository _materialsRepository;
    private readonly IQuizzesRepository _quizzesRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<AddItemHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public AddItemHandler(
        ICollectionsRepository collectionsRepository,
        ICollectionSectionsRepository sectionsRepository,
        ICollectionItemsRepository itemsRepository,
        IMaterialsRepository materialsRepository,
        IQuizzesRepository quizzesRepository,
        ITransactionManager transactionManager,
        ILogger<AddItemHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _sectionsRepository = sectionsRepository;
        _itemsRepository = itemsRepository;
        _materialsRepository = materialsRepository;
        _quizzesRepository = quizzesRepository;
        _transactionManager = transactionManager;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        AddItemCommand command,
        CancellationToken cancellationToken)
    {
        // null/пустой itemType → MATERIAL (back-compat со старыми payload'ами `{materialId}` эпохи
        // material-only подборок; сам ключ переименован в referenceId — фронт обновляет ST-15/16).
        string rawItemType = string.IsNullOrWhiteSpace(command.Request.ItemType)
            ? nameof(CollectionItemType.MATERIAL)
            : command.Request.ItemType;
        if (!Enum.TryParse(rawItemType, ignoreCase: true, out CollectionItemType itemType))
            return EducationErrors.InvalidCollectionItemType(command.Request.ItemType ?? string.Empty);

        Result<Collection, Error> collectionResult = await _collectionsRepository.GetByAsync(
            c => c.Id == command.CollectionId, cancellationToken);
        if (collectionResult.IsFailure)
            return collectionResult.Error;

        Collection collection = collectionResult.Value;

        UnitResult<Error> ownership = _userScopedData.CheckOwnership(collection.AuthorId);
        if (ownership.IsFailure)
            return ownership.Error;

        Result<CollectionSection, Error> sectionResult = await _sectionsRepository.GetByAsync(
            s => s.Id == command.SectionId && s.CollectionId == collection.Id,
            cancellationToken: cancellationToken);
        if (sectionResult.IsFailure)
            return EducationErrors.ItemNotFound("CollectionSection", command.SectionId);

        // Существование референса — explicit-чек вместо снятого FK (generic-ссылка не может
        // иметь FK на две таблицы). Ownership референса НЕ проверяется — зеркало материалов:
        // материалы и раньше клались в подборку без проверки автора (cross-author items
        // легитимны, замок решает per-item entitlement в detail).
        Guid referenceId = command.Request.ReferenceId;
        bool referenceExists = itemType switch
        {
            CollectionItemType.MATERIAL => await _materialsRepository.ExistsAsync(
                m => m.Id == referenceId, cancellationToken),
            CollectionItemType.QUIZ => await _quizzesRepository.ExistsAsync(
                q => q.Id == referenceId, cancellationToken),
            _ => false,
        };
        if (!referenceExists)
        {
            return itemType == CollectionItemType.QUIZ
                ? EducationErrors.QuizNotFound(referenceId)
                : EducationErrors.MaterialNotFound(referenceId);
        }

        bool duplicate = await _itemsRepository.ExistsInSectionAsync(
            command.SectionId, itemType, referenceId, cancellationToken);
        if (duplicate)
            return EducationErrors.CollectionItemDuplicate(referenceId);

        Result<CollectionItem, Error> lastResult = await _itemsRepository.GetByAsync(
            i => i.SectionId == command.SectionId,
            orderBy: i => i.SortKey,
            descending: true,
            cancellationToken);

        SortKey sortKey = lastResult.IsSuccess
            ? SortKey.After(lastResult.Value.SortKey)
            : SortKey.Initial();

        var item = new CollectionItem(command.SectionId, itemType, referenceId, sortKey);
        await _itemsRepository.AddAsync(item, cancellationToken);
        collection.Touch();

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Item {ItemId} ({ItemType} {ReferenceId}) added to section {SectionId}",
            item.Id, itemType, referenceId, command.SectionId);

        return item.Id;
    }
}
