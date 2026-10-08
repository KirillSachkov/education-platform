using System.Net;
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
///     Ручная приёмка задачи студенту, который НИКОГДА не сдавал работу (#398). Автор/админ/модератор
///     отмечает задание выполненным — создаётся синтетический принятый submission и прогоняется
///     обычный каскад XP/project/module.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class MarkIssueCompleteForUserEndpointTests : ProgressServiceTestsBase
{
    public MarkIssueCompleteForUserEndpointTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task MarkCompleteForUser_WhenUserNeverSubmitted_ShouldCreateApprovedSubmissionAndAwardXpOnce()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId);

        // Привилегированный ревьюер. Студент НЕ делал ничего — ни enroll, ни start, ни submit.
        AuthenticateAs(reviewerId, "platform-moderator");
        NoOpOutboxService.Reset();

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/mark-complete-for-user",
            new MarkIssueCompleteForUserRequest(studentId, "Принято вручную"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.IssueProgressId == issueProgress!.Id));
        ProjectProgress? projectProgress = await ExecuteInDb(dbContext =>
            dbContext.ProjectProgresses.FirstOrDefaultAsync(x => x.ProjectId == projectId));
        ModuleItemProgress? moduleItemProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleItemProgresses.FirstOrDefaultAsync(x =>
                x.ReferenceId == issueId && x.ItemType == ModuleItemProgressType.ISSUE));
        ModuleProgress? moduleProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleProgresses.FirstOrDefaultAsync(x => x.ModuleId == moduleId));

        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);

        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);
        Assert.Equal(reviewerId, submission.ReviewerId);

        Assert.NotNull(projectProgress);
        Assert.Equal(ProjectProgressStatus.COMPLETED, projectProgress.Status);
        Assert.Equal(1, projectProgress.TotalIssuesCompleted);

        Assert.NotNull(moduleItemProgress);
        Assert.Equal(ModuleItemProgressStatus.COMPLETED, moduleItemProgress.Status);

        Assert.NotNull(moduleProgress);
        Assert.Equal(ModuleProgressStatus.COMPLETED, moduleProgress.Status);
        Assert.Equal(1, moduleProgress.ItemsCompleted);

        // XP начислен РОВНО один раз (ledger keyed by issueProgress.Id).
        int xpAwardCount = await ExecuteInDb(dbContext =>
            dbContext.XpAwards.CountAsync(x =>
                x.UserId == studentId
                && x.AwardType == XpAwardType.ISSUE_APPROVED
                && x.SourceId == issueProgress!.Id));
        Assert.Equal(1, xpAwardCount);

        UserGamificationStats? stats = await ExecuteInDb(dbContext =>
            dbContext.UserGamificationStats.FirstOrDefaultAsync(x => x.UserId == studentId));
        Assert.NotNull(stats);
        Assert.True(stats.TotalXp > 0);

        // L2: integration event issue_submission.approved уходит в outbox для NotificationService.
        Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved approved =
            NoOpOutboxService.Published
                .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved>()
                .Single();
        Assert.Equal(submission.Id, approved.SubmissionId);
        Assert.Equal(studentId, approved.UserId);
        Assert.Equal(issueId, approved.IssueId);
        Assert.Equal(courseId, approved.CourseId);
        Assert.Equal(reviewerId, approved.ReviewerId);

        // Синтетический submission НЕ публикует awaiting-review (нет ложной AI-проверки / уведомления).
        Assert.Empty(NoOpOutboxService.Published
            .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionAwaitingReview>());
    }

    [Fact]
    public async Task MarkCompleteForUser_WhenCalledTwice_ShouldBeIdempotentAndNotDoubleXp()
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

        HttpResponseMessage firstResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/mark-complete-for-user",
            new MarkIssueCompleteForUserRequest(studentId));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        IssueProgress? issueProgressAfterFirst = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        Assert.NotNull(issueProgressAfterFirst);

        int xpBefore = await ExecuteInDb(dbContext =>
            dbContext.XpAwards.CountAsync(x =>
                x.UserId == studentId
                && x.AwardType == XpAwardType.ISSUE_APPROVED
                && x.SourceId == issueProgressAfterFirst!.Id));
        Assert.Equal(1, xpBefore);

        long totalXpBefore = await ExecuteInDb(async dbContext =>
        {
            UserGamificationStats? stats = await dbContext.UserGamificationStats
                .FirstOrDefaultAsync(x => x.UserId == studentId);
            return stats?.TotalXp ?? 0;
        });

        NoOpOutboxService.Reset();

        // Повторный вызов — задача уже COMPLETED → no-op, XP не дублируется, approve-event не уходит.
        HttpResponseMessage secondResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/mark-complete-for-user",
            new MarkIssueCompleteForUserRequest(studentId));
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        IssueProgress? issueProgressAfterSecond = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        Assert.NotNull(issueProgressAfterSecond);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgressAfterSecond.Status);

        int xpAfter = await ExecuteInDb(dbContext =>
            dbContext.XpAwards.CountAsync(x =>
                x.UserId == studentId
                && x.AwardType == XpAwardType.ISSUE_APPROVED
                && x.SourceId == issueProgressAfterSecond!.Id));
        Assert.Equal(1, xpAfter);

        long totalXpAfter = await ExecuteInDb(async dbContext =>
        {
            UserGamificationStats? stats = await dbContext.UserGamificationStats
                .FirstOrDefaultAsync(x => x.UserId == studentId);
            return stats?.TotalXp ?? 0;
        });
        Assert.Equal(totalXpBefore, totalXpAfter);

        Assert.Empty(NoOpOutboxService.Published
            .OfType<Shared.Messaging.IntegrationEvents.Progress.Events.IssueSubmissionApproved>());
    }

    [Fact]
    public async Task MarkCompleteForUser_WhenCallerLacksReviewPrivilege_ShouldReturnForbidden()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId: null);

        // Обычный участник без progress.manage — отбивается на permission-гейте.
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/mark-complete-for-user",
            new MarkIssueCompleteForUserRequest(studentId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MarkCompleteForUser_WhenAdminNotCourseAuthor_ShouldSucceedViaPrivilegeBypass()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId: null);

        // Админ — не автор курса. Привилегированный bypass (isPrivileged) пропускает независимо
        // от авторства. (AUTHOR-owner ветка хэндлера — defensive parity с ApproveIssue: endpoint
        // гейтнут Progress.MANAGE, которого у чистого platform-author нет, поэтому до неё доходит
        // только мульти-ролевой пользователь — отдельным тестом не изолируется.)
        AuthenticateAs(Guid.NewGuid(), "platform-admin");

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/mark-complete-for-user",
            new MarkIssueCompleteForUserRequest(studentId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);
    }

    [Fact]
    public async Task MarkCompleteForUser_WhenServiceTokenWithReviewerOverride_ShouldSucceedAndRecordReviewer()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId: null);

        // Service-токен (mcp-admin, client_credentials) имеет sub=client_id → UserId=Guid.Empty (#505).
        // Привилегированный caller передаёт явный ReviewerId-override, который записывается принявшим.
        AuthenticateAs(Guid.Empty, "platform-admin");

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/mark-complete-for-user",
            new MarkIssueCompleteForUserRequest(studentId, ReviewerId: reviewerId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueProgress? issueProgress = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        Assert.NotNull(issueProgress);
        Assert.Equal(IssueProgressStatus.COMPLETED, issueProgress.Status);

        IssueSubmission? submission = await ExecuteInDb(dbContext =>
            dbContext.IssueSubmissions.FirstOrDefaultAsync(x => x.IssueProgressId == issueProgress!.Id));
        Assert.NotNull(submission);
        Assert.Equal(IssueSubmissionReviewStatus.APPROVED, submission.ReviewStatus);
        Assert.Equal(reviewerId, submission.ReviewerId);
    }

    [Fact]
    public async Task MarkCompleteForUser_WhenServiceTokenWithoutReviewerOverride_ShouldReturnBadRequest()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedCourseIssueContext(courseId, authorId, projectId, issueId, moduleId: null);

        // Service-токен без ReviewerId-override: некого записать принявшим → fail-closed 400
        // ДО любых side-effect'ов. Не падаем 500, не создаём phantom-progress.
        AuthenticateAs(Guid.Empty, "platform-admin");

        HttpResponseMessage response = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/mark-complete-for-user",
            new MarkIssueCompleteForUserRequest(studentId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Phantom-progress guard: цепочка не должна быть закоммичена при fail-closed.
        IssueProgress? phantom = await ExecuteInDb(dbContext =>
            dbContext.IssueProgresses.FirstOrDefaultAsync(x => x.IssueId == issueId));
        Assert.Null(phantom);

        CourseEnrollment? phantomEnrollment = await ExecuteInDb(dbContext =>
            dbContext.CourseEnrollments.FirstOrDefaultAsync(x => x.UserId == studentId && x.CourseId == courseId));
        Assert.Null(phantomEnrollment);
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
