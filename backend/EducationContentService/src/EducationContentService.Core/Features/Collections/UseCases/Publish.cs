using Core.Abstractions;
using Core.Database;
using EducationContentService.Core.Database;
using EducationContentService.Domain.Collections;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.Collections.UseCases;

public sealed record PublishCollectionCommand(Guid CollectionId) : ICommand;

public sealed class PublishCollectionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("collections/{collectionId:guid}/publish", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromServices] PublishCollectionHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new PublishCollectionCommand(collectionId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class PublishCollectionHandler : ICommandHandler<Guid, PublishCollectionCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ICollectionItemsRepository _itemsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly ILogger<PublishCollectionHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public PublishCollectionHandler(
        ICollectionsRepository collectionsRepository,
        ICollectionItemsRepository itemsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        ILogger<PublishCollectionHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _itemsRepository = itemsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        PublishCollectionCommand command,
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

        bool hasAnyItem = await _itemsRepository.HasAnyForCollectionAsync(
            collection.Id, cancellationToken);

        UnitResult<Error> publishResult = collection.Publish(hasAnyItem);
        if (publishResult.IsFailure)
            return publishResult.Error;

        await _outbox.PublishAsync(new CollectionPublished(collection.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        await CollectionCourseCacheInvalidation.InvalidateAsync(
            _cache, collection, _logger, cancellationToken);

        _logger.LogInformation("Collection {CollectionId} published", collection.Id);

        return collection.Id;
    }
}
