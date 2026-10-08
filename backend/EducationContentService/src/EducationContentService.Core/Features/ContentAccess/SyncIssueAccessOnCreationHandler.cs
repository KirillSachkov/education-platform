using ContentAccess;
using EducationContentService.Core.Features.ProjectItems;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

public static class SyncIssueAccessOnCreationHandler
{
    public static Task HandleAsync(
        IssueCreated message,
        IIssuesRepository issuesRepository,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger,
        CancellationToken ct) =>
        IssueAccessSync.SyncAsync(
            message.IssueId,
            issuesRepository,
            resourceAccessWriter,
            logger,
            ct);
}
