using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace ProgressService.IntegrationTests.Features.Issues;

[Collection(nameof(IntegrationTestsFixture))]
public class IssueWorkflowEndpointsTests : ProgressServiceTestsBase
{
    public IssueWorkflowEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task StartIssueWork_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task StartIssueWork_ShouldCreateIssueProgress_And_ModuleItemProgress()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        SeedCourseIssueContext(courseId, projectId, issueId, moduleId);

        await EnrollAsync(courseId, userId);
        await StartProjectAsync(courseId, projectId);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.EnrollmentId ==
                                                              dbContext.CourseEnrollments
                                                                  .Where(e => e.UserId == userId && e.CourseId == courseId)
                                                                  .Select(e => e.Id)
                                                                  .First()
                                                              && x.IssueId == issueId));

        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ReferenceId == issueId && x.ItemType == ModuleItemProgressType.ISSUE));

        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.IN_PROGRESS, issueProgress.Status);
        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.NOT_COMPLETED, moduleItemProgress.Status);
        Assert.Equal(moduleId, moduleItemProgress.ModuleId);
    }

    [Fact]
    public async Task SubmitIssue_ShouldCreateSubmission_And_MoveIssueToUnderReview()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);

        await EnrollAsync(courseId, userId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(response);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.PENDING, submission.ReviewStatus);
        Assert.Equal(1, submission.AttemptNumber.Value);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.UNDER_REVIEW, issueProgress.Status);
    }

    [Fact]
    public async Task SubmitIssue_WithoutSubmitIssuesCapability_ShouldReturn403_AndNotCreateSubmission()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);

        await EnrollAsync(courseId, userId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        EntitlementChecker.DenyCapability(userId, "SUBMIT_ISSUES");

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("issue.submission.not.allowed.by.plan", await ReadErrorCodeAsync(response));

        int submissionCount = await ExecuteInDb(dbContext => dbContext.IssueSubmissions.CountAsync());
        Assert.Equal(0, submissionCount);
    }

    [Fact]
    public async Task SubmitIssue_WithNonPullRequestGitHubUrl_ShouldReturn400_AndNotCreateSubmission()
    {
        // #718 — студент вставил страницу «создать PR» (`/pull/new/...`), а не реальный PR.
        // ARS требует `/pull/{N}`, поэтому такая ссылка не создаёт AiReview и автор видит пустой
        // AI-статус. Сдача должна отвергаться на входе. Валидный PR покрыт тестом выше.
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);

        await EnrollAsync(courseId, userId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/wolonee/DirectoryService/pull/new/DS-F15"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("issue.submission.not_pull_request", await ReadErrorCodeAsync(response));

        // Ни одной сдачи не создано, задача осталась в работе (транзакция не коммитилась).
        int submissionCount = await ExecuteInDb(dbContext => dbContext.IssueSubmissions.CountAsync());
        Assert.Equal(0, submissionCount);

        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.IN_PROGRESS, issueProgress.Status);
    }

    [Fact]
    public async Task SubmitIssue_SelfCheck_ShouldApproveSubmission_And_CompleteIssue()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        SeedCourseIssueContext(
            courseId,
            projectId,
            issueId,
            moduleId: null,
            submissionMode: "SELF_CHECK",
            selfCheckInstructions: "Проверьте, что решение запускается локально.");

        await EnrollAsync(courseId, userId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        NoOpOutboxService.Reset();

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest(ContentPayload: "Я запустил решение локально и проверил критерии."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(response);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);
        Assert.Equal(userId, submission.ReviewerId);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);
        Assert.Empty(NoOpOutboxService.Published.OfType<IssueSubmissionAwaitingReview>());
    }

    [Fact]
    public async Task StartIssueWork_WithoutStartedProject_ShouldAutoCreateProjectAndModuleProgress()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        SeedCourseIssueContext(courseId, projectId, issueId, moduleId);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ProjectProgress? projectProgress = await ExecuteInDb(dbContext =>
            dbContext.ProjectProgresses.FirstOrDefaultAsync(x => x.ProjectId == projectId));
        ModuleProgress? moduleProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleProgresses.FirstOrDefaultAsync(x => x.ModuleId == moduleId));
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ReferenceId == issueId && x.ItemType == ModuleItemProgressType.ISSUE));

        Assert.NotNull(projectProgress);
        Assert.Equal(ProjectProgressStatus.IN_PROGRESS, projectProgress.Status);
        Assert.NotNull(moduleProgress);
        Assert.Equal(ModuleProgressStatus.IN_PROGRESS, moduleProgress.Status);
        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.NOT_COMPLETED, moduleItemProgress.Status);
    }

    [Fact]
    public async Task StartIssueWork_RepeatedCall_ShouldBeIdempotent()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        SeedCourseIssueContext(courseId, projectId, issueId, moduleId);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage firstResponse = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage secondResponse = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        int issueProgressCount = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.CountAsync(x => x.IssueId == issueId));
        int moduleItemProgressCount = await ExecuteInDb(dbContext =>
            dbContext.ModuleItemProgresses.CountAsync(x =>
                x.ReferenceId == issueId && x.ItemType == ModuleItemProgressType.ISSUE));

        Assert.Equal(1, issueProgressCount);
        Assert.Equal(1, moduleItemProgressCount);
    }

    private void SeedCourseIssueContext(
        Guid courseId,
        Guid projectId,
        Guid issueId,
        Guid? moduleId,
        string submissionMode = "PULL_REQUEST",
        string? selfCheckInstructions = null)
    {
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddProject(courseId, projectId, 1);
        if (moduleId is not null)
        {
            EducationContentClient.AddModule(courseId, moduleId.Value, 1);
        }

        EducationContentClient.AddIssue(
            projectId,
            issueId,
            moduleId,
            submissionMode,
            selfCheckInstructions);
    }

    private Task EnrollAsync(Guid courseId, Guid userId) => SeedEnrollmentAsync(courseId, userId);

    private Task StartProjectAsync(Guid courseId, Guid projectId) =>
        PostAsync($"/progress/courses/{courseId}/projects/{projectId}/start");

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement messages = document.RootElement.GetProperty("error").GetProperty("messages");
        return messages[0].GetProperty("code").GetString()!;
    }
}
