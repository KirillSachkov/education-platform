using System.Net;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Issues;

[Collection(nameof(IntegrationTestsFixture))]
public class IssueSubmissionHistoryEndpointsTests : ProgressServiceTestsBase
{
    public IssueSubmissionHistoryEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetIssueSubmissionHistory_WhenIssueNotStarted_ShouldReturnEmptyAttempts()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedLearningCourse(courseId, moduleId, lessonId, projectId, issueId);

        AuthenticateAs(studentId, "platform-admin");
        await EnrollAsync(courseId, studentId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueSubmissionHistoryDto dto =
            await ReadWrappedResultAsync<IssueSubmissionHistoryDto>(response);

        Assert.Equal(issueId, dto.IssueId);
        Assert.Equal("NOT_STARTED", dto.CurrentStatus);
        Assert.Empty(dto.Attempts);
        Assert.Null(dto.StartedAt);
        Assert.Null(dto.CompletedAt);
    }

    [Fact]
    public async Task GetIssueSubmissionHistory_WhenIssueStartedWithoutSubmissions_ShouldReturnInProgress()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedLearningCourse(courseId, moduleId, lessonId, projectId, issueId);

        AuthenticateAs(studentId, "platform-admin");
        await EnrollAsync(courseId, studentId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueSubmissionHistoryDto dto =
            await ReadWrappedResultAsync<IssueSubmissionHistoryDto>(response);

        Assert.Equal(issueId, dto.IssueId);
        Assert.Equal("IN_PROGRESS", dto.CurrentStatus);
        Assert.NotNull(dto.StartedAt);
        Assert.Empty(dto.Attempts);
    }

    [Fact]
    public async Task GetIssueSubmissionHistory_ShouldReturnRequestedChangesAttempt()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedLearningCourse(courseId, moduleId, lessonId, projectId, issueId);

        AuthenticateAs(studentId, "platform-admin");
        await EnrollAsync(courseId, studentId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/10"));
        SubmitIssueResponse submission = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submission.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submission.SubmissionId}/request-changes",
            new RequestIssueChangesRequest("Нужно поправить тесты"));

        AuthenticateAs(studentId, "platform-admin");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueSubmissionHistoryDto dto =
            await ReadWrappedResultAsync<IssueSubmissionHistoryDto>(response);

        Assert.Equal("REQUESTED_CHANGES", dto.CurrentStatus);

        IssueSubmissionHistoryItemDto attempt = Assert.Single(dto.Attempts);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal("CHANGES_REQUESTED", attempt.ReviewStatus);
        Assert.Equal("Нужно поправить тесты", attempt.Feedback);
    }

    [Fact]
    public async Task GetIssueSubmissionHistory_ShouldReturnAttemptsInOrder()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedLearningCourse(courseId, moduleId, lessonId, projectId, issueId);

        AuthenticateAs(studentId, "platform-admin");
        await EnrollAsync(courseId, studentId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        HttpResponseMessage firstSubmitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/11"));
        SubmitIssueResponse firstSubmission =
            await ReadWrappedResultAsync<SubmitIssueResponse>(firstSubmitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{firstSubmission.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{firstSubmission.SubmissionId}/request-changes",
            new RequestIssueChangesRequest("Нужно доработать первую попытку"));

        AuthenticateAs(studentId, "platform-admin");
        HttpResponseMessage secondSubmitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/12"));
        SubmitIssueResponse secondSubmission =
            await ReadWrappedResultAsync<SubmitIssueResponse>(secondSubmitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{secondSubmission.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{secondSubmission.SubmissionId}/approve",
            new ApproveIssueRequest("Вторая попытка принята"));

        AuthenticateAs(studentId, "platform-admin");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueSubmissionHistoryDto dto =
            await ReadWrappedResultAsync<IssueSubmissionHistoryDto>(response);

        Assert.Equal("COMPLETED", dto.CurrentStatus);
        Assert.Equal(2, dto.Attempts.Count);

        IssueSubmissionHistoryItemDto firstAttempt = dto.Attempts[0];
        IssueSubmissionHistoryItemDto secondAttempt = dto.Attempts[1];

        Assert.Equal(1, firstAttempt.AttemptNumber);
        Assert.Equal("CHANGES_REQUESTED", firstAttempt.ReviewStatus);
        Assert.Equal("Нужно доработать первую попытку", firstAttempt.Feedback);

        Assert.Equal(2, secondAttempt.AttemptNumber);
        Assert.Equal("APPROVED", secondAttempt.ReviewStatus);
        Assert.Equal("Вторая попытка принята", secondAttempt.Feedback);
    }

    private void SeedLearningCourse(Guid courseId, Guid moduleId, Guid lessonId, Guid projectId, Guid issueId)
    {
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddModule(courseId, moduleId, 2);
        EducationContentClient.AddMaterial(moduleId, lessonId, 2);
        EducationContentClient.AddProject(courseId, projectId, 1);
        EducationContentClient.AddIssue(projectId, issueId, moduleId);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Demo course",
            "Demo description",
            totalModules: 1,
            totalMaterials: 1,
            totalUniqueIssues: 1,
            materialIds: [lessonId]);
    }

    private Task EnrollAsync(Guid courseId, Guid userId) => SeedEnrollmentAsync(courseId, userId);
}
