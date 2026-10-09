using System.Net;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Reviews;

[Collection(nameof(IntegrationTestsFixture))]
public class ReviewWorkflowEndpointsTests : ProgressServiceTestsBase
{
    public ReviewWorkflowEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task RequestChanges_ShouldMoveSubmissionAndIssueToRequestedChanges()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        HttpResponseMessage startReviewResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");
        HttpResponseMessage requestChangesResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/request-changes",
            new RequestIssueChangesRequest("Исправь замечания"));

        Assert.Equal(HttpStatusCode.OK, startReviewResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, requestChangesResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.CHANGES_REQUESTED, submission.ReviewStatus);
        Assert.Equal(reviewerId, submission.ReviewerId);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.REQUESTED_CHANGES, issueProgress.Status);
    }

    [Fact]
    public async Task RequestChanges_WhenIssueProgressStatusIsCompleted_ShouldFailAndNotCommitSubmissionChanges()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");

        await ExecuteInDb(dbContext =>
            dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE issue_progress SET status = {0} WHERE issue_id = {1}",
                IssueProgressStatus.COMPLETED.ToString(),
                issueId));

        HttpResponseMessage requestChangesResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/request-changes",
            new RequestIssueChangesRequest("Исправь замечания"));

        Assert.Equal(HttpStatusCode.Conflict, requestChangesResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.IN_REVIEW, submission.ReviewStatus);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);
    }

    [Fact]
    public async Task ApproveIssue_ShouldCompleteIssueProjectAndModule()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await StartModuleAsync(courseId, moduleId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");

        HttpResponseMessage approveResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/approve",
            new ApproveIssueRequest("Ок"));

        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        ProjectProgress? projectProgress = await ExecuteInDb(dbContext =>
            dbContext.ProjectProgresses.FirstOrDefaultAsync(x => x.ProjectId == projectId));
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ReferenceId == issueId && x.ItemType == ModuleItemProgressType.ISSUE));
        ModuleProgress? moduleProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleProgresses.FirstOrDefaultAsync(x => x.ModuleId == moduleId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);
        Assert.NotNull(projectProgress);
        Assert.Equal(ProjectProgressStatus.COMPLETED, projectProgress.Status);
        Assert.Equal(1, projectProgress.TotalIssuesCompleted);
        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, moduleItemProgress.Status);
        Assert.NotNull(moduleProgress);
        Assert.Equal(ModuleProgressStatus.COMPLETED, moduleProgress.Status);
        Assert.Equal(1, moduleProgress.ItemsCompleted);
    }

    [Fact]
    public async Task ApproveIssue_WhenIssueProgressStatusIsCompleted_ShouldFailAndNotCommitSubmissionApproval()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");

        await ExecuteInDb(dbContext =>
            dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE issue_progress SET status = {0} WHERE issue_id = {1}",
                IssueProgressStatus.COMPLETED.ToString(),
                issueId));

        HttpResponseMessage approveResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/approve",
            new ApproveIssueRequest("Ок"));

        Assert.Equal(HttpStatusCode.Conflict, approveResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.IN_REVIEW, submission.ReviewStatus);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);
    }

    [Fact]
    public async Task ApproveIssue_WhenProjectProgressMissing_ShouldFailAndNotCommit()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await StartModuleAsync(courseId, moduleId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");

        await ExecuteInDb(async dbContext =>
        {
            ProjectProgress? projectProgress = await dbContext.ProjectProgresses
                .FirstOrDefaultAsync(x => x.ProjectId == projectId);

            Assert.NotNull(projectProgress);

            dbContext.ProjectProgresses.Remove(projectProgress);
            await dbContext.SaveChangesAsync();
        });

        HttpResponseMessage approveResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/approve",
            new ApproveIssueRequest("Ок"));

        Assert.Equal(HttpStatusCode.NotFound, approveResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.IN_REVIEW, submission.ReviewStatus);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.UNDER_REVIEW, issueProgress.Status);
    }

    [Fact]
    public async Task RequestChanges_WithoutFeedback_ShouldSucceed()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");

        HttpResponseMessage requestChangesResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/request-changes",
            new RequestIssueChangesRequest(null));

        Assert.Equal(HttpStatusCode.OK, requestChangesResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.CHANGES_REQUESTED, submission.ReviewStatus);
        Assert.Null(submission.Feedback);
    }

    [Fact]
    public async Task ReopenReview_FromChangesRequested_ShouldMoveBackToInReview()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/request-changes",
            new RequestIssueChangesRequest("Исправь"));

        HttpResponseMessage reopenResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/reopen");

        Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.IN_REVIEW, submission.ReviewStatus);
        Assert.Null(submission.Feedback);
        Assert.Null(submission.ReviewedAt);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.UNDER_REVIEW, issueProgress.Status);
        Assert.Null(issueProgress.CompletedAt);
    }

    [Fact]
    public async Task ReopenReview_FromApproved_ShouldRevertProjectAndModuleProgress()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await StartModuleAsync(courseId, moduleId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/approve",
            new ApproveIssueRequest("Принято"));

        // Сохраняем pre-reopen состояние для дельт.

        HttpResponseMessage reopenResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/reopen");

        Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        ProjectProgress? projectProgress = await ExecuteInDb(dbContext =>
            dbContext.ProjectProgresses.FirstOrDefaultAsync(x => x.ProjectId == projectId));
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ReferenceId == issueId && x.ItemType == ModuleItemProgressType.ISSUE));
        ModuleProgress? moduleProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleProgresses.FirstOrDefaultAsync(x => x.ModuleId == moduleId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.IN_REVIEW, submission.ReviewStatus);
        Assert.Null(submission.Feedback);
        Assert.Null(submission.ReviewedAt);

        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.UNDER_REVIEW, issueProgress.Status);
        Assert.Null(issueProgress.CompletedAt);

        Assert.NotNull(projectProgress);
        Assert.Equal(0, projectProgress.TotalIssuesCompleted);
        Assert.Equal(ProjectProgressStatus.IN_PROGRESS, projectProgress.Status);
        Assert.Null(projectProgress.CompletedAt);

        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.NOT_COMPLETED, moduleItemProgress.Status);
        Assert.Null(moduleItemProgress.CompletedAt);

        Assert.NotNull(moduleProgress);
        Assert.Equal(0, moduleProgress.ItemsCompleted);
        Assert.Equal(ModuleProgressStatus.IN_PROGRESS, moduleProgress.Status);

    }

    /// <summary>
    /// Регрессия #454: задание исчезало из ВСЕХ вкладок ревью после «вернуть в ревью».
    /// Сценарий — AI-авто-проверенная попытка: ARS-гейт выставил <c>ready_for_human_review=false</c>,
    /// AI-вердикт авто-Approve'нул её (флаг остался false). Reopen такой попытки давал
    /// <c>IN_REVIEW + ready_for_human_review=false</c> — состояние, которое не выбирает ни один из
    /// трёх listing-фильтров (pending требует ready=TRUE, in-review требует PENDING, reviewed требует
    /// терминальный статус). Работа пропадала. Фикс: <c>ReopenReview()</c> снимает AI-гейт
    /// (<c>ready_for_human_review=true</c>), и reopened-сабмишн виден в pending-инбоксе.
    /// </summary>
    [Fact]
    public async Task ReopenReview_WhenSubmissionWasAiGated_ShouldStayVisibleInPendingList()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/approve",
            new ApproveIssueRequest("AI: принято"));

        // Имитируем AI-авто-приёмку: GateForAiReview() выставил ready_for_human_review=false,
        // а Approve() флаг не возвращает — попытка APPROVED, но всё ещё гейтнута для человека.
        await ExecuteInDb(dbContext =>
            dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE issue_submissions SET ready_for_human_review = FALSE WHERE id = {0}",
                submitIssueResponse.SubmissionId));

        HttpResponseMessage reopenResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/reopen");
        Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);

        // Регрессия: reopened-работа обязана быть видимой хотя бы в одном списке. Pending-инбокс
        // показывает IN_REVIEW только при ready_for_human_review=TRUE — до фикса сабмишн сюда не попадал.
        HttpResponseMessage pendingResponse = await AppHttpClient.GetAsync(
            "/progress/reviews/issues/pending?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        ReviewIssuesPagedResponse pending = await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(pendingResponse);
        Assert.Contains(pending.Items, x => x.SubmissionId == submitIssueResponse.SubmissionId);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.IN_REVIEW, submission.ReviewStatus);
        Assert.True(submission.ReadyForHumanReview);
    }

    [Fact]
    public async Task ReopenReview_FromPendingOrInReview_ShouldReturnConflict()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");

        // PENDING → reopen запрещён.
        HttpResponseMessage reopenFromPending = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/reopen");
        Assert.Equal(HttpStatusCode.Conflict, reopenFromPending.StatusCode);

        // IN_REVIEW → reopen тоже запрещён.
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");
        HttpResponseMessage reopenFromInReview = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/reopen");
        Assert.Equal(HttpStatusCode.Conflict, reopenFromInReview.StatusCode);
    }

    [Fact]
    public async Task MarkComplete_FromPending_ShouldForceApproveAndCompleteIssueProjectModule()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await StartModuleAsync(courseId, moduleId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        // Submission остаётся PENDING (никто не звал start-review) — привилегированный
        // ревьюер (moderator) форс-принимает одной кнопкой.
        AuthenticateAs(reviewerId, "platform-moderator");
        NoOpOutboxService.Reset();

        HttpResponseMessage markCompleteResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/mark-complete",
            new ApproveIssueRequest("Принято вручную"));

        Assert.Equal(HttpStatusCode.OK, markCompleteResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        ProjectProgress? projectProgress = await ExecuteInDb(dbContext =>
            dbContext.ProjectProgresses.FirstOrDefaultAsync(x => x.ProjectId == projectId));
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ReferenceId == issueId && x.ItemType == ModuleItemProgressType.ISSUE));
        ModuleProgress? moduleProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleProgresses.FirstOrDefaultAsync(x => x.ModuleId == moduleId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);
        Assert.Equal(reviewerId, submission.ReviewerId);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);
        Assert.NotNull(projectProgress);
        Assert.Equal(ProjectProgressStatus.COMPLETED, projectProgress.Status);
        Assert.Equal(1, projectProgress.TotalIssuesCompleted);
        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, moduleItemProgress.Status);
        Assert.NotNull(moduleProgress);
        Assert.Equal(ModuleProgressStatus.COMPLETED, moduleProgress.Status);

        // L2: integration event issue_submission.approved уходит в outbox для NotificationService.
        Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved approved =
            NoOpOutboxService.Published
                .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved>()
                .Single();
        Assert.Equal(submitIssueResponse.SubmissionId, approved.SubmissionId);
        Assert.Equal(studentId, approved.UserId);
        Assert.Equal(issueId, approved.IssueId);
        Assert.Equal(courseId, approved.CourseId);
        Assert.Equal(reviewerId, approved.ReviewerId);
    }

    [Fact]
    public async Task MarkComplete_FromChangesRequested_ShouldForceApprove()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/request-changes",
            new RequestIssueChangesRequest("Доработай"));

        // Ревьюер передумал — принимает работу как есть из CHANGES_REQUESTED.
        HttpResponseMessage markCompleteResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/mark-complete",
            new ApproveIssueRequest(null));

        Assert.Equal(HttpStatusCode.OK, markCompleteResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);
    }

    [Fact]
    public async Task MarkComplete_WhenAlreadyApproved_ShouldBeIdempotent()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/approve",
            new ApproveIssueRequest("Ок"));

        NoOpOutboxService.Reset();

        HttpResponseMessage markCompleteResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/mark-complete",
            new ApproveIssueRequest(null));

        Assert.Equal(HttpStatusCode.OK, markCompleteResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);

        // Идемпотентно — повторного approve-event нет.
        Assert.Empty(NoOpOutboxService.Published
            .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved>());
    }

    [Fact]
    public async Task MarkComplete_WhenCallerLacksReviewPrivilege_ShouldReturnForbidden()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        // Обычный участник без progress.manage — отбивается на permission-гейте (как и весь
        // review-surface: list + approve/start/reopen/mark-complete все на Progress.MANAGE).
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage markCompleteResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/mark-complete",
            new ApproveIssueRequest(null));

        Assert.Equal(HttpStatusCode.Forbidden, markCompleteResponse.StatusCode);
    }

    /// <summary>
    /// Регрессия desync (#383): IssueProgress шарится между попытками. Старая попытка остаётся
    /// reopenable (APPROVED), а более новая попытка той же задачи уже перевела shared
    /// IssueProgress в UNDER_REVIEW. Reopen старой попытки не должен падать с
    /// «Невозможно выполнить ReopenReview из статуса UNDER_REVIEW» — каскад идемпотентно no-op'ит.
    /// </summary>
    [Fact]
    public async Task ReopenReview_WhenIssueProgressUnderReviewByNewerAttempt_ShouldSucceed()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");

        // Попытка #1 — submit + approve (APPROVED, IssueProgress → COMPLETED).
        HttpResponseMessage submit1 = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse attempt1 = await ReadWrappedResultAsync<SubmitIssueResponse>(submit1);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{attempt1.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{attempt1.SubmissionId}/approve",
            new ApproveIssueRequest("Ок"));

        // Имитируем ситуацию мульти-попыток: более новая попытка/AI продвинула shared
        // IssueProgress обратно в UNDER_REVIEW (как это сделал бы SubmitForReview новой попытки),
        // пока попытка #1 остаётся APPROVED и формально reopenable.
        await ExecuteInDb(dbContext =>
            dbContext.Database.ExecuteSqlRawAsync(
                "UPDATE issue_progress SET status = {0}, completed_at = NULL WHERE issue_id = {1}",
                IssueProgressStatus.UNDER_REVIEW.ToString(),
                issueId));

        // Reopen попытки #1 — раньше падал Conflict из-за каскада IssueProgress.ReopenReview()
        // на UNDER_REVIEW. Теперь — 200, каскад идемпотентен.
        HttpResponseMessage reopenResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{attempt1.SubmissionId}/reopen");

        Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == attempt1.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.IN_REVIEW, submission.ReviewStatus);
        Assert.NotNull(issueProgress);
        // Shared IssueProgress остаётся UNDER_REVIEW (newer attempt держит его там) — каскад no-op.
        Assert.Equal(IssueProgressStatus.UNDER_REVIEW, issueProgress.Status);
    }

    // #668 — «Отказаться от проверки»: ревьюер снимает себя с взятой в работу попытки,
    // она возвращается в PENDING («Ожидает проверки») и снова видна в pending-инбоксе.
    [Fact]
    public async Task CancelReview_FromInReview_ShouldReturnToPendingAndReappearInPendingList()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");

        HttpResponseMessage cancelResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/cancel-review");

        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        // Снова видна в «Ожидают проверки» (pending: review_status IN (PENDING,IN_REVIEW) AND ready=TRUE).
        HttpResponseMessage pendingResponse = await AppHttpClient.GetAsync(
            "/progress/reviews/issues/pending?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, pendingResponse.StatusCode);
        ReviewIssuesPagedResponse pending = await ReadWrappedResultAsync<ReviewIssuesPagedResponse>(pendingResponse);
        Assert.Contains(pending.Items, x => x.SubmissionId == submitIssueResponse.SubmissionId);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.Id == submitIssueResponse.SubmissionId));
        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.PENDING, submission.ReviewStatus);
        Assert.Null(submission.ReviewerId);
        Assert.Null(submission.ReviewStartedAt);
        Assert.True(submission.ReadyForHumanReview);
        // IssueProgress не трогается отказом от проверки.
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.UNDER_REVIEW, issueProgress.Status);
    }

    [Fact]
    public async Task CancelReview_FromPendingOrApproved_ShouldReturnConflict()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");

        // PENDING → cancel запрещён.
        HttpResponseMessage cancelFromPending = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/cancel-review");
        Assert.Equal(HttpStatusCode.Conflict, cancelFromPending.StatusCode);

        // APPROVED → cancel тоже запрещён (только из IN_REVIEW).
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/approve",
            new ApproveIssueRequest("Ок"));
        HttpResponseMessage cancelFromApproved = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/cancel-review");
        Assert.Equal(HttpStatusCode.Conflict, cancelFromApproved.StatusCode);
    }

    [Fact]
    public async Task CancelReview_WhenCallerLacksReviewPrivilege_ShouldReturnForbidden()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, projectId, issueId, moduleId: null);
        AuthenticateAs(studentId, "platform-admin");

        await EnrollAsync(courseId, studentId);
        await StartProjectAsync(courseId, projectId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));
        SubmitIssueResponse submitIssueResponse = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        // Обычный участник без progress.manage — отбивается на permission-гейте.
        // (NB: handler'ный `isAuthorOwner` branch — author + enrollment.AuthorId==user — через HTTP
        // недостижим: `platform-author` имеет только Progress.VIEW, а endpoint требует Progress.MANAGE,
        // т.е. автор отсекается на permission-гейте раньше handler'а. Эта ветка — defensive-зеркало
        // `reopen`/`mark-complete`; ни один из них её через эндпоинт тоже не покрывает. Поэтому
        // тест на положительный author-owner кейс физически не написать без MANAGE у автора.)
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage cancelResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submitIssueResponse.SubmissionId}/cancel-review");

        Assert.Equal(HttpStatusCode.Forbidden, cancelResponse.StatusCode);
    }

    [Fact]
    public async Task CancelReview_WhenSubmissionDoesNotExist_ShouldReturnNotFound()
    {
        Guid courseId = Guid.NewGuid();
        AuthenticateAs(Guid.NewGuid(), "platform-admin");

        HttpResponseMessage cancelResponse = await PostAsync(
            $"/progress/courses/{courseId}/reviews/issues/{Guid.NewGuid()}/cancel-review");

        Assert.Equal(HttpStatusCode.NotFound, cancelResponse.StatusCode);
    }

    private void SeedCourseIssueContext(Guid courseId, Guid projectId, Guid issueId, Guid? moduleId)
    {
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddProject(courseId, projectId, 1);
        if (moduleId is not null)
        {
            EducationContentClient.AddModule(courseId, moduleId.Value, 1);
        }

        EducationContentClient.AddIssue(projectId, issueId, moduleId);
    }

    private Task EnrollAsync(Guid courseId, Guid userId) => SeedEnrollmentAsync(courseId, userId);

    private Task StartProjectAsync(Guid courseId, Guid projectId) =>
        PostAsync($"/progress/courses/{courseId}/projects/{projectId}/start");

    private Task StartModuleAsync(Guid courseId, Guid moduleId) =>
        PostAsync($"/progress/courses/{courseId}/modules/{moduleId}/start");
}