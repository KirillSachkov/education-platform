using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Reviews;

/// <summary>
///     Ручной staff-override статуса задачи студенту (#518). Автор/админ/модератор выставляет ЛЮБОЙ
///     статус прогресса из полной палитры, минуя обычный workflow. COMPLETED → синтетический принятый
///     submission + каскад XP/project/module + integration event; уход из COMPLETED → откат XP/project/
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
    public async Task SetStatus_TargetCompleted_NeverStarted_ShouldRollUpAndAwardXpAndPublishApproved()
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

        int xpAwardCount = await ExecuteInDb(db =>
            db.XpAwards.CountAsync(x =>
                x.UserId == studentId
                && x.AwardType == XpAwardType.ISSUE_APPROVED
                && x.SourceId == issueProgress!.Id));
        Assert.Equal(1, xpAwardCount);

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
    public async Task SetStatus_TargetCompleted_Twice_ShouldBeIdempotentNoDoubleXp()
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
        long totalXpBefore = await TotalXpAsync(studentId);
        Assert.Equal(1, await XpCountAsync(studentId, issueProgress.Id));

        NoOpOutboxService.Reset();

        Assert.Equal(HttpStatusCode.OK,
            (await SetStatusAsync(courseId, issueId, new SetIssueStatusForUserRequest(studentId, "COMPLETED"))).StatusCode);

        Assert.Equal(IssueProgressStatus.COMPLETED,
            (await ExecuteInDb(db => db.IssueProgresses.FirstAsync(x => x.IssueId == issueId))).Status);
        Assert.Equal(1, await XpCountAsync(studentId, issueProgress.Id));
        Assert.Equal(totalXpBefore, await TotalXpAsync(studentId));
        Assert.Empty(NoOpOutboxService.Published
            .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved>());
    }

    [Fact]
    public async Task SetStatus_CompletedToNotStarted_ShouldRollBackXpAndProjectAndModule()
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
        long totalXpAfterComplete = await TotalXpAsync(studentId);

        HttpResponseMessage rollback = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, "NOT_STARTED"));
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);

        IssueProgress after = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));
        Assert.Equal(IssueProgressStatus.NOT_STARTED, after.Status);
        Assert.Null(after.StartedAt);
        Assert.Null(after.CompletedAt);

        // ISSUE_APPROVED XP откатился (revoke удаляет ledger-строку этого источника). Total XP
        // снизился (зеркалит каноничный ReopenReview — ModuleCompleted/ProjectCompleted XP сам
        // reopen-каскад не отзывает, поэтому проверяем < , а не == 0).
        Assert.Equal(0, await XpCountAsync(studentId, issueProgress.Id));
        Assert.True(await TotalXpAsync(studentId) < totalXpAfterComplete);

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
    public async Task SetStatus_CompletedToInProgress_ShouldRollBackXp()
    {
        await AssertRollbackFromCompletedAsync("IN_PROGRESS", IssueProgressStatus.IN_PROGRESS);
    }

    [Fact]
    public async Task SetStatus_CompletedToRequestedChanges_ShouldRollBackXp()
    {
        await AssertRollbackFromCompletedAsync("REQUESTED_CHANGES", IssueProgressStatus.REQUESTED_CHANGES);
    }

    [Fact]
    public async Task SetStatus_CompletedToUnderReview_ShouldRollBackXp()
    {
        await AssertRollbackFromCompletedAsync("UNDER_REVIEW", IssueProgressStatus.UNDER_REVIEW);
    }

    [Fact]
    public async Task SetStatus_NotStartedToInProgress_ShouldSetStatusNoSubmissionNoXp()
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

        // Никакого submission'а / approve-event / XP.
        Assert.Equal(0, await ExecuteInDb(db =>
            db.IssueSubmissions.CountAsync(x => x.IssueProgressId == issueProgress.Id)));
        Assert.Equal(0, await XpCountAsync(studentId, issueProgress.Id));
        Assert.Equal(0, await TotalXpAsync(studentId));
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
        Assert.Equal(0, await XpCountAsync(studentId, issueProgress.Id));
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
    ///     откат XP + статус. Общий хелпер для IN_PROGRESS / REQUESTED_CHANGES / UNDER_REVIEW.
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
        Assert.Equal(1, await XpCountAsync(studentId, issueProgress.Id));
        long totalXpAfterComplete = await TotalXpAsync(studentId);

        HttpResponseMessage rollback = await SetStatusAsync(
            courseId, issueId, new SetIssueStatusForUserRequest(studentId, targetRaw));
        Assert.Equal(HttpStatusCode.OK, rollback.StatusCode);

        IssueProgress after = await ExecuteInDb(db =>
            db.IssueProgresses.FirstAsync(x => x.IssueId == issueId));
        Assert.Equal(expected, after.Status);
        Assert.Null(after.CompletedAt);

        // ISSUE_APPROVED XP откатился (revoke удаляет ledger-строку источника); total XP снизился
        // (каноничный ReopenReview не отзывает Module/ProjectCompleted XP — проверяем <, не == 0).
        Assert.Equal(0, await XpCountAsync(studentId, issueProgress.Id));
        Assert.True(await TotalXpAsync(studentId) < totalXpAfterComplete);

        ProjectProgress projectProgress = await ExecuteInDb(db =>
            db.ProjectProgresses.FirstAsync(x => x.ProjectId == projectId));
        Assert.Equal(0, projectProgress.TotalIssuesCompleted);
    }

    private Task<int> XpCountAsync(Guid userId, Guid sourceId) =>
        ExecuteInDb(db => db.XpAwards.CountAsync(x =>
            x.UserId == userId && x.AwardType == XpAwardType.ISSUE_APPROVED && x.SourceId == sourceId));

    private async Task<long> TotalXpAsync(Guid userId)
    {
        return await ExecuteInDb(async db =>
        {
            UserGamificationStats? stats = await db.UserGamificationStats
                .FirstOrDefaultAsync(x => x.UserId == userId);
            return stats?.TotalXp ?? 0;
        });
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
