using FileService.Core.Services.AssetRegistry;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace FileService.Core.Features.AssetRegistry.IntegrationEvents;

public sealed class IssueHardDeletedHandler
{
    private readonly AssetDeletionLifecycleService _deletionService;
    private readonly ILogger<IssueHardDeletedHandler> _logger;

    public IssueHardDeletedHandler(
        ILogger<IssueHardDeletedHandler> logger,
        AssetDeletionLifecycleService deletionService)
    {
        _logger = logger;
        _deletionService = deletionService;
    }

    public async Task Handle(IssueHardDeleted message, CancellationToken cancellationToken)
    {
        int deleted = await _deletionService.DeleteByTargetEntityAsync("issue", message.IssueId, cancellationToken);

        _logger.LogInformation(
            "IssueHardDeleted: Deleted {Count} file(s) for issue {IssueId}",
            deleted,
            message.IssueId);
    }
}
