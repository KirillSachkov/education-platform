using FileService.Core.Services.AssetRegistry;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace FileService.Core.Features.AssetRegistry.IntegrationEvents;

public sealed class CollectionHardDeletedHandler
{
    private readonly AssetDeletionLifecycleService _deletionService;
    private readonly ILogger<CollectionHardDeletedHandler> _logger;

    public CollectionHardDeletedHandler(
        ILogger<CollectionHardDeletedHandler> logger,
        AssetDeletionLifecycleService deletionService)
    {
        _logger = logger;
        _deletionService = deletionService;
    }

    public async Task Handle(CollectionHardDeleted message, CancellationToken cancellationToken)
    {
        int deleted = await _deletionService.DeleteByTargetEntityAsync(
            "collection", message.CollectionId, cancellationToken);

        _logger.LogInformation(
            "CollectionHardDeleted: Deleted {Count} asset(s) for collection {CollectionId}",
            deleted,
            message.CollectionId);
    }
}
