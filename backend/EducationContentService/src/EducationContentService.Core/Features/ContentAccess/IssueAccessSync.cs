using ContentAccess;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Domain.Projects;

namespace EducationContentService.Core.Features.ContentAccess;

internal static class IssueAccessSync
{
    public static async Task SyncAsync(
        Guid issueId,
        IIssuesRepository issuesRepository,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger,
        CancellationToken ct)
    {
        Result<Issue, Error> issueResult = await issuesRepository.GetByAsync(x => x.Id == issueId, ct);
        if (issueResult.IsFailure)
        {
            logger.LogInformation(
                "Issue {IssueId} no longer exists; stale access-sync message ignored",
                issueId);
            return;
        }

        Issue issue = issueResult.Value;
        List<Guid> courseIds = await issuesRepository.GetCourseIdsAsync(issueId, ct);
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            issue.AccessType,
            issue.Id,
            courseIds,
            logger);

        await resourceAccessWriter.SetTagsAsync(ResourceTypes.ISSUE, issue.Id, tags, ct);
        logger.LogInformation(
            "Synced authoritative issue access tags. IssueId={IssueId}, AccessType={AccessType}, Tags=[{Tags}]",
            issue.Id,
            issue.AccessType,
            string.Join(", ", tags));
    }
}
