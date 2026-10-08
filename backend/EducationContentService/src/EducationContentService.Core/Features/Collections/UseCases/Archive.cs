using Core.Abstractions;
using Core.Database;
using EducationContentService.Domain.Collections;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.Collections.UseCases;

public sealed record ArchiveCollectionCommand(Guid CollectionId) : ICommand;

public sealed class ArchiveCollectionEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("collections/{collectionId:guid}/archive", async Task<EndpointResult<Guid>> (
                    [FromRoute] Guid collectionId,
                    [FromServices] ArchiveCollectionHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new ArchiveCollectionCommand(collectionId), cancellationToken))
            .RequirePermissions(PlatformPermissions.Lessons.MANAGE);
    }
}

public sealed class ArchiveCollectionHandler : ICommandHandler<Guid, ArchiveCollectionCommand>
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly HybridCache _cache;
    private readonly ILogger<ArchiveCollectionHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public ArchiveCollectionHandler(
        ICollectionsRepository collectionsRepository,
        ITransactionManager transactionManager,
        HybridCache cache,
        ILogger<ArchiveCollectionHandler> logger,
        UserScopedData userScopedData)
    {
        _collectionsRepository = collectionsRepository;
        _transactionManager = transactionManager;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<Guid, Error>> Handle(
        ArchiveCollectionCommand command,
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

        UnitResult<Error> archiveResult = collection.Archive();
        if (archiveResult.IsFailure)
            return archiveResult.Error;

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        await CollectionCourseCacheInvalidation.InvalidateAsync(
            _cache, collection, _logger, cancellationToken);

        _logger.LogInformation("Collection {CollectionId} archived", collection.Id);

        return collection.Id;
    }
}
