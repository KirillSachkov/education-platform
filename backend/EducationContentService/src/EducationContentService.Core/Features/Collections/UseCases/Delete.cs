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

public sealed record DeleteCollectionCommand(Guid CollectionId) : ICommand;

public sealed class DeleteCollectionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("collections/{collectionId:guid}", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromServices] DeleteCollectionHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new DeleteCollectionCommand(collectionId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class DeleteCollectionHandler : ICommandHandler<Guid, DeleteCollectionCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly HybridCache _cache;
    private readonly ILogger<DeleteCollectionHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public DeleteCollectionHandler(
        ICollectionsRepository collectionsRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        HybridCache cache,
        ILogger<DeleteCollectionHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        DeleteCollectionCommand command,
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

        _collectionsRepository.Delete(collection);

        await _outbox.PublishAsync(new CollectionHardDeleted(collection.Id));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        await CollectionCourseCacheInvalidation.InvalidateAsync(
            _cache, collection, _logger, cancellationToken);

        _logger.LogInformation("Collection {CollectionId} hard-deleted", collection.Id);

        return collection.Id;
    }
}
