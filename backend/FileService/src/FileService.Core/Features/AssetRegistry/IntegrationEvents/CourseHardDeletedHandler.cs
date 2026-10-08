using FileService.Core.Services.AssetRegistry;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace FileService.Core.Features.AssetRegistry.IntegrationEvents;

public sealed class CourseHardDeletedHandler
{
    private readonly AssetDeletionLifecycleService _deletionService;
    private readonly ILogger<CourseHardDeletedHandler> _logger;

    public CourseHardDeletedHandler(
        ILogger<CourseHardDeletedHandler> logger,
        AssetDeletionLifecycleService deletionService)
    {
        _logger = logger;
        _deletionService = deletionService;
    }

    public async Task Handle(CourseHardDeleted message, CancellationToken cancellationToken)
    {
        int deleted = await _deletionService.DeleteByTargetEntityAsync(
            "course", message.CourseId, cancellationToken);

        _logger.LogInformation(
            "CourseHardDeleted: Deleted {Count} asset(s) for course {CourseId}",
            deleted,
            message.CourseId);
    }
}
