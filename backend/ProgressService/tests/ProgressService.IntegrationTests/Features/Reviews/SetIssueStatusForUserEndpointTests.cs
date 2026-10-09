using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Reviews;

/// <summary>
///     Ручной staff-override статуса задачи студенту (#518). Автор/админ/модератор выставляет ЛЮБОЙ
///     статус прогресса из полной палитры, минуя обычный workflow. COMPLETED → синтетический принятый
///     module; промежуточные статусы (NOT_STARTED / IN_PROGRESS / UNDER_REVIEW / REQUESTED_CHANGES) —
///     только переключение статуса без submission'а.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class SetIssueStatusForUserEndpointTests : ProgressServiceTestsBase
{
    public SetIssueStatusForUserEndpointTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    private Task<HttpResponseMessage> SetStatusAsync(
        Guid courseId,
        Guid issueId,
        SetIssueStatusForUserRequest request) =>
        AppHttpClient.PutAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/progress-status-for-user/",
            request);

    [Fact]
    public async Task SetStatus_TargetCompleted_NeverStarted_ShouldRollUpAndPublishApproved()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId);

        AuthenticateAs(reviewerId, "platform-moderator");
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await SetStatusAsync(
            courseId,
            issueId,
            new SetIssueStatusForUserRequest(studentId, "COMPLETED"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueProgress? issueProgress = await ExecuteInDb(db =>
            db.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);

        IssueSubmission? submission = await ExecuteInDb(db =>
            db.IssueSubmissions.FirstOrDefaultAsync(x => x.IssueProgressId == issueProgress!.Id));
        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);
        Assert.Equal(reviewerId, submission.ReviewerId);

        ProjectProgress? projectProgress = await ExecuteInDb(db =>
            db.ProjectProgresses.FirstOrDefaultAsync(x => x.ProjectId == projectId));
        Assert.NotNull(projectProgress);
        Assert.Equal(ProjectProgressStatus.COMPLETED, projectProgress.Status);
        Assert.Equal(1, projectProgress.TotalIssuesCompleted);

        ModuleItemProgress? moduleItem = await ExecuteInDb(db =>
            db.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ReferenceId == issueId && x.ItemType == ModuleItemProgressType.ISSUE));
        Assert.NotNull(moduleItem);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, moduleItem.Status);

        Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved approved =
            NoOpOutboxService.Published
                .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved>()
                .Single();
        Assert.Equal(submission.Id, approved.SubmissionId);
        Assert.Equal(studentId, approved.UserId);
        Assert.Equal(issueId, approved.IssueId);
        Assert.Equal(courseId, approved.CourseId);
        Assert.Equal(reviewerId, approved.ReviewerId);

        Assert.Empty(NoOpOutboxService.Published
            .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionAwaitingReview>());
    }

    [Fact]
    public async Task SetStatus_TargetCompleted_Twice_ShouldBeIdempotent()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId);
        AuthenticateAs(reviewerId, "platform-moderator");

        Assert.Equal(HttpStatusCode.OK,
            (await SetStatusAsync(courseId, issueId, new SetIssueStatusForUserRequest(studentId, "COMPLETED"))).StatusCode);

        IssueProgress issueProgress = (await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId)));

        NoOpOutboxService.Reset();

        Assert.Equal(HttpStatusCode.OK,
            (await SetStatusAsync(courseId, issueId, new SetIssueStatusForUserRequest(studentId, "COMPLETED"))).StatusCode);

        Assert.Equal(IssueProgressStatus.COMPLETED,
            (await ExecuteInDb(db => db.IssueProgresses.FirstAsync(x => x.IssueId == issueId))).Status);
        Assert.Empty(NoOpOutboxService.Published
            .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved>());
    }

    [Fact]
    public async Task SetStatus_CompletedToNotStarted_ShouldRollBackProjectAndModule()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId);
        AuthenticateAs(reviewerId, "platform-moderator");

        Assert.Equal(HttpStatusCode.OK,
            (await SetStatusAsync(courseId, issueId, new SetIssueStatusForUserRequest(studentId, "COMPLETED"))).StatusCode);

        IssueProgress issueProgress = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));

        HttpResponseMessage rollback = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, "NOT_STARTED"));
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);

        IssueProgress after = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));
        Assert.Equal(IssueProgressStatus.NOT_STARTED, after.Status);
        Assert.Null(after.StartedAt);
        Assert.Null(after.CompletedAt);

        // reopen-каскад не отзывает, поэтому проверяем < , а не == 0).

        // ProjectProgress декрементирован.
        ProjectProgress projectProgress = await ExecuteInDb(db =>
            db.ProjectProgresses.FirstAsync(x => x.ProjectId == projectId));
        Assert.Equal(0, projectProgress.TotalIssuesCompleted);
        Assert.NotEqual(ProjectProgressStatus.COMPLETED, projectProgress.Status);

        // ModuleItem откатился.
        ModuleItemProgress moduleItem = await ExecuteInDb(db =>
            db.ModuleItemProgresses.FirstAsync(x =>
                x.ReferenceId == issueId && x.ItemType == ModuleItemProgressType.ISSUE));
        Assert.NotEqual(ModuleItemProgressStatus.COMPLETED, moduleItem.Status);
    }

    [Fact]
    public async Task SetStatus_CompletedToInProgress_ShouldRollBackProgress()
    {
        await AssertRollbackFromCompletedAsync("IN_PROGRESS", IssueProgressStatus.IN_PROGRESS);
    }

    [Fact]
    public async Task SetStatus_CompletedToRequestedChanges_ShouldRollBackProgress()
    {
        await AssertRollbackFromCompletedAsync("REQUESTED_CHANGES", IssueProgressStatus.REQUESTED_CHANGES);
    }

    [Fact]
    public async Task SetStatus_CompletedToUnderReview_ShouldRollBackProgress()
    {
        await AssertRollbackFromCompletedAsync("UNDER_REVIEW", IssueProgressStatus.UNDER_REVIEW);
    }

    [Fact]
    public async Task SetStatus_NotStartedToInProgress_ShouldSetStatusNoSubmission()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId);
        AuthenticateAs(reviewerId, "platform-moderator");
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, "IN_PROGRESS"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueProgress issueProgress = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));
        Assert.Equal(IssueProgressStatus.IN_PROGRESS, issueProgress.Status);

        Assert.Equal(0, await ExecuteInDb(db =>
            db.IssueSubmissions.CountAsync(x => x.IssueProgressId == issueProgress.Id)));
        Assert.Empty(NoOpOutboxService.Published
            .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved>());
    }

    [Fact]
    public async Task SetStatus_SameStatus_ShouldBeIdempotentNoOp()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId: null);
        AuthenticateAs(reviewerId, "platform-moderator");

        // NOT_STARTED → NOT_STARTED для никогда-не-начинавшего студента: no-op, прогресс-якорь
        // создаётся (lazy anchor), но IssueProgress остаётся NOT_STARTED, событий нет.
        HttpResponseMessage response = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, "NOT_STARTED"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueProgress issueProgress = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));
        Assert.Equal(IssueProgressStatus.NOT_STARTED, issueProgress.Status);
    }

    [Fact]
    public async Task SetStatus_Completed_ServiceTokenWithReviewerOverride_ShouldSucceed()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId: null);

        // Service-токен → UserId=Guid.Empty (#505). Override обязателен для COMPLETED.
        AuthenticateAs(Guid.Empty, "platform-admin");

        HttpResponseMessage response = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, "COMPLETED", ReviewerId: reviewerId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueProgress issueProgress = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);

        IssueSubmission submission = await ExecuteInDb(db =>
            db.IssueSubmissions.FirstAsync(x => x.IssueProgressId == issueProgress.Id));
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);
        Assert.Equal(reviewerId, submission.ReviewerId);
    }

    [Fact]
    public async Task SetStatus_Completed_ServiceTokenWithoutReviewerOverride_ShouldReturnBadRequestNoPhantom()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId: null);
        AuthenticateAs(Guid.Empty, "platform-admin");

        HttpResponseMessage response = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, "COMPLETED"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Fail-closed ДО side-effect'ов: ни IssueProgress, ни enrollment-anchor не созданы.
        Assert.Null(await ExecuteInDb(db =>
            db.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId)));
        Assert.Null(await ExecuteInDb(db =>
            db.CourseEnrollments.FirstOrDefaultAsync(x => x.UserId == studentId && x.CourseId == courseId)));
    }

    [Fact]
    public async Task SetStatus_WhenCallerLacksPrivilege_ShouldReturnForbidden()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId: null);
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, "IN_PROGRESS"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetStatus_AdminNotCourseAuthor_ShouldSucceedViaPrivilegeBypass()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId: null);

        // Админ — не автор курса. Привилегированный bypass пропускает.
        AuthenticateAs(Guid.NewGuid(), "platform-admin");

        HttpResponseMessage response = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, "IN_PROGRESS"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueProgress issueProgress = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));
        Assert.Equal(IssueProgressStatus.IN_PROGRESS, issueProgress.Status);
    }

    /// <summary>
    ///     Завершает задачу через endpoint, затем переводит в <paramref name="targetRaw"/> и проверяет
    /// </summary>
    private async Task AssertRollbackFromCompletedAsync(string targetRaw, IssueProgressStatus expected)
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId);
        AuthenticateAs(reviewerId, "platform-moderator");

        Assert.Equal(HttpStatusCode.OK,
            (await SetStatusAsync(courseId, issueId, new SetIssueStatusForUserRequest(studentId, "COMPLETED"))).StatusCode);

        IssueProgress issueProgress = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));

        HttpResponseMessage rollback = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, targetRaw));
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);

        IssueProgress after = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));
        Assert.Equal(expected, after.Status);
        Assert.Null(after.CompletedAt);

        ProjectProgress projectProgress = await ExecuteInDb(db =>
            db.ProjectProgresses.FirstAsync(x => x.ProjectId == projectId));
        Assert.Equal(0, projectProgress.TotalIssuesCompleted);
    }

    private void SeedCourseIssueContext(Guid courseId, Guid authorId, Guid projectId, Guid issueId, Guid? moduleId)
    {
        EducationContentClient.AddCourse(courseId, authorId, hasFreeContent: true);
        EducationContentClient.AddProject(courseId, projectId, 1);
        if (moduleId is not null)
        {
            EducationContentClient.AddModule(courseId, moduleId.Value, 1);
        }

        EducationContentClient.AddIssue(projectId, issueId, moduleId);
    }
}