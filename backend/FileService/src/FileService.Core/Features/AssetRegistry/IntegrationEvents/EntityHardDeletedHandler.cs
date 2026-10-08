using FileService.Core.Services.AssetRegistry;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace FileService.Core.Features.AssetRegistry.IntegrationEvents;

public sealed class EntityHardDeletedHandler
{
    private readonly AssetDeletionLifecycleService _deletionService;
    private readonly ILogger<EntityHardDeletedHandler> _logger;

    public EntityHardDeletedHandler(
        ILogger<EntityHardDeletedHandler> logger,
        AssetDeletionLifecycleService deletionService)
    {
        _logger = logger;
        _deletionService = deletionService;
    }

    public async Task Handle(EntityHardDeleted message, CancellationToken cancellationToken)
    {
        int deleted = await _deletionService.DeleteByTargetEntityAsync(
            message.EntityType, message.EntityId, cancellationToken);

        _logger.LogInformation(
            "EntityHardDeleted: Deleted {Count} asset(s) for {EntityType} {EntityId}",
            deleted,
            message.EntityType,
            message.EntityId);
    }
}
