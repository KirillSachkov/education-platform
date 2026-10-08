using AuthService.Contracts;
using AuthService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Projects;
using ProgressService.Contracts.Dtos;

namespace ProgressService.Core.Features.Reviews;

internal static class ReviewUserEnricher
{
    public static async Task<List<ReviewIssueItemDto>> EnrichWithUserInfoAsync(
        List<ReviewIssueItemDto> items,
        IAuthServiceClient authServiceClient,
        IEducationContentServiceClient ecsClient,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return items;
        }

        List<Guid> userIds = items
            .SelectMany(i => i.ReviewerId.HasValue
                ? new[] { i.StudentId, i.ReviewerId.Value }
                : new[] { i.StudentId })
            .Distinct()
            .ToList();

        Guid[] courseIds = items.Select(i => i.CourseId).Distinct().ToArray();
        Guid[] projectIds = items.Select(i => i.ProjectId).Distinct().ToArray();
        Guid[] issueIds = items.Select(i => i.IssueId).Distinct().ToArray();

        Task<Result<IReadOnlyList<AuthUserLookupDto>, Error>> usersTask =
            authServiceClient.GetUsersByIdsAsync(userIds, cancellationToken);
        Task<Result<IReadOnlyList<CourseTitleDto>, Error>> coursesTask =
            ecsClient.GetCourseTitlesAsync(courseIds, cancellationToken);
        Task<Result<IReadOnlyList<ProjectTitleDto>, Error>> projectsTask =
            ecsClient.GetProjectTitlesAsync(projectIds, cancellationToken);
        Task<Result<IReadOnlyList<IssueTitleDto>, Error>> issuesTask =
            ecsClient.GetIssueTitlesAsync(issueIds, cancellationToken);

        await Task.WhenAll(usersTask, coursesTask, projectsTask, issuesTask);

        Result<IReadOnlyList<AuthUserLookupDto>, Error> usersResult = await usersTask;
        Result<IReadOnlyList<CourseTitleDto>, Error> coursesResult = await coursesTask;
        Result<IReadOnlyList<ProjectTitleDto>, Error> projectsResult = await projectsTask;
        Result<IReadOnlyList<IssueTitleDto>, Error> issuesResult = await issuesTask;

        Dictionary<Guid, AuthUserLookupDto> userById = usersResult.IsSuccess
            ? usersResult.Value.ToDictionary(u => u.UserId)
            : [];
        Dictionary<Guid, string> courseTitleById = coursesResult.IsSuccess
            ? coursesResult.Value.ToDictionary(c => c.CourseId, c => c.Title)
            : [];
        Dictionary<Guid, string> projectTitleById = projectsResult.IsSuccess
            ? projectsResult.Value.ToDictionary(p => p.ProjectId, p => p.Title)
            : [];
        Dictionary<Guid, string> issueTitleById = issuesResult.IsSuccess
            ? issuesResult.Value.ToDictionary(i => i.IssueId, i => i.Title)
            : [];

        return items.Select(item =>
        {
            userById.TryGetValue(item.StudentId, out AuthUserLookupDto? student);
            AuthUserLookupDto? reviewer = item.ReviewerId.HasValue &&
                                          userById.TryGetValue(item.ReviewerId.Value, out var r)
                ? r
                : null;

            return item with
            {
                StudentName = student?.Name,
                StudentUsername = student?.Username,
                StudentEmail = student?.Email,
                StudentTelegramUsername = student?.TelegramUsername,
                StudentAvatarId = student?.AvatarId,
                ReviewerName = reviewer?.Name,
                ReviewerUsername = reviewer?.Username,
                ReviewerAvatarId = reviewer?.AvatarId,
                CourseTitle = courseTitleById.GetValueOrDefault(item.CourseId),
                ProjectTitle = projectTitleById.GetValueOrDefault(item.ProjectId),
                IssueTitle = issueTitleById.GetValueOrDefault(item.IssueId),
            };
        }).ToList();
    }
}
