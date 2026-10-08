using ContentAccess;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

public static class ClearIssueAccessOnDeletionHandler
{
    public static async Task HandleAsync(
        IssueHardDeleted message,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger,
        CancellationToken ct)
    {
        await resourceAccessWriter.ClearTagsAsync(ResourceTypes.ISSUE, message.IssueId, ct);
        logger.LogInformation("Cleared access tags for deleted issue {IssueId}", message.IssueId);
    }
}
