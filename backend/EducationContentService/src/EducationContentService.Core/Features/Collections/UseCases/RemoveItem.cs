using Core.Abstractions;
using Core.Database;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.UseCases;

public sealed record RemoveItemCommand(Guid CollectionId, Guid SectionId, Guid ItemId) : ICommand;

public sealed class RemoveItemEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("collections/{collectionId:guid}/sections/{sectionId:guid}/items/{itemId:guid}",
                async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromRoute] Guid sectionId,
                    [FromRoute] Guid itemId,
                    [FromServices] RemoveItemHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(
                    new RemoveItemCommand(collectionId, sectionId, itemId),
                    cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class RemoveItemHandler : ICommandHandler<Guid, RemoveItemCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICollectionSectionsRepository _sectionsRepository;
    private readonly ICollectionItemsRepository _itemsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<RemoveItemHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public RemoveItemHandler(
        ICollectionsRepository collectionsRepository,
        ICollectionSectionsRepository sectionsRepository,
        ICollectionItemsRepository itemsRepository,
        ITransactionManager transactionManager,
        ILogger<RemoveItemHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _sectionsRepository = sectionsRepository;
        _itemsRepository = itemsRepository;
        _transactionManager = transactionManager;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        RemoveItemCommand command,
        CancellationToken cancellationToken)
    {
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

        Result<CollectionItem, Error> itemResult = await _itemsRepository.GetByAsync(
            i => i.Id == command.ItemId && i.SectionId == command.SectionId,
            cancellationToken: cancellationToken);
        if (itemResult.IsFailure)
            return EducationErrors.ItemNotFound("CollectionItem", command.ItemId);

        _itemsRepository.Delete(itemResult.Value);
        collection.Touch();

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation(
            "Item {ItemId} removed from section {SectionId} in collection {CollectionId}",
            command.ItemId, command.SectionId, collection.Id);

        return command.ItemId;
    }
}
