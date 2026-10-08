using Core.Database;
using EducationContentService.Core.Features.Collections;
using EducationContentService.Domain.Collections;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace EducationContentService.Core.Features.FileEvents;

public sealed class CollectionCoverDeletedHandler
{
    private readonly ICollectionsRepository _collectionsRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly ILogger<CollectionCoverDeletedHandler> _logger;

    public CollectionCoverDeletedHandler(
        ICollectionsRepository collectionsRepository,
        ITransactionManager transactionManager,
        ILogger<CollectionCoverDeletedHandler> logger)
    {
        _collectionsRepository = collectionsRepository;
        _transactionManager = transactionManager;
        _logger = logger;
    }

    public async Task Handle(FileAssetDeleted message, CancellationToken ct)
    {
        if (!string.Equals(message.TargetEntityType, FileEventsRouting.EntityTypes.COLLECTION, StringComparison.Ordinal)
            || !string.Equals(message.UsageType, FileEventsRouting.UsageTypes.COLLECTION_COVER, StringComparison.Ordinal))
            return;

        if (message.TargetEntityId is null)
            return;

        Result<Collection, Error> collectionResult = await _collectionsRepository
            .GetByAsync(c => c.Id == message.TargetEntityId.Value, ct);
        if (collectionResult.IsFailure)
        {
            _logger.LogInformation(
                "Collection {CollectionId} not found for FileAssetDeleted {AssetId} — late event, no-op",
                message.TargetEntityId, message.AssetId);
            return;
        }

        if (collectionResult.Value.CoverImageId != message.AssetId ||
            collectionResult.Value.CoverBindingRevision != message.BindingRevision)
            return;

        collectionResult.Value.DetachImage();
        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(ct);
        if (save.IsFailure)
            throw save.Error.AsTransient().ToException();
    }
}
