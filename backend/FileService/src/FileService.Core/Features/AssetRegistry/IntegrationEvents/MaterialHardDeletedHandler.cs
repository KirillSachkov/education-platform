using FileService.Core.Services.AssetRegistry;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace FileService.Core.Features.AssetRegistry.IntegrationEvents;

public sealed class MaterialHardDeletedHandler
{
    private readonly AssetDeletionLifecycleService _deletionService;
    private readonly ILogger<MaterialHardDeletedHandler> _logger;

    public MaterialHardDeletedHandler(
        ILogger<MaterialHardDeletedHandler> logger,
        AssetDeletionLifecycleService deletionService)
    {
        _logger = logger;
        _deletionService = deletionService;
    }

    public async Task Handle(MaterialHardDeleted message, CancellationToken cancellationToken)
    {
        int deleted = await _deletionService.DeleteByTargetEntityAsync("material", message.MaterialId, cancellationToken);

        _logger.LogInformation(
            "MaterialHardDeleted: Deleted {Count} file(s) for material {MaterialId}",
            deleted,
            message.MaterialId);
    }
}
