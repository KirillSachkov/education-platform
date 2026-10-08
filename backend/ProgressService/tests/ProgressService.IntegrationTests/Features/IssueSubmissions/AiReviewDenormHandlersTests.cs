using Microsoft.EntityFrameworkCore;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Gamification;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Projects;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace ProgressService.IntegrationTests.Features.IssueSubmissions;

/// <summary>
///     Phase 8 (#15): Wolverine consumers для assignment_review.events.
///     L1 tests — invoke handlers напрямую через <c>InvokeMessageAndWaitAsync</c>.
/// </summary>
public sealed class AiReviewDenormHandlersTests : ProgressServiceTestsBase
{
    public AiReviewDenormHandlersTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task IterationCompleted_MinorIssues_AutoApproves_AndPopulatesDenorm()
    {
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedSubmissionAsync(userId);

        // Вердикт-политика #383: MINOR_ISSUES из PENDING → авто-Approve (мелкие/необязательные
        // замечания не блокируют ученика). Денорм-поля заполняются в той же транзакции.
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;
        await InvokeMessageAndWaitAsync(new AiReviewIterationCompleted(
            AiReviewId: Guid.NewGuid(),
            IterationId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            IterationNumber: 1,
            Verdict: "MINOR_ISSUES",
            GitHubReviewId: 100L,
            CompletedAt: completedAt));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.Equal(IssueSubmissionReviewStatus.APPROVED, s.ReviewStatus);
            Assert.Equal("MINOR_ISSUES", s.LatestAiVerdict);
            Assert.Equal(1, s.AiIterationsCount);
            Assert.NotNull(s.LastAiIterationAt);
            Assert.Equal("READY", s.AiReviewStatus);
        });
    }

    [Theory]
    [InlineData("LOOKS_GOOD")]    // студент закрыл замечания → denorm повышается до LOOKS_GOOD
    [InlineData("MINOR_ISSUES")]  // всё ещё есть мелочи → остаётся MINOR
    [InlineData("MAJOR_ISSUES")]  // даже «ухудшение» не отбирает зачёт
    public async Task IterationCompleted_OnAlreadyApproved_UpdatesDenormOnly_KeepsCompletedAndXp(
        string rerunVerdict)
    {
        // #725: студент дорабатывает замечания после MINOR-approve и запускает повторную
        // AI-проверку. Приходит вторая итерация по уже-APPROVED submission — ApplyVerdictGate
        // срабатывает только из PENDING, поэтому статус/XP не трогаются, обновляется лишь denorm.
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedSubmissionAsync(userId);

        // Итерация 1: MINOR_ISSUES → авто-Approve → submission APPROVED, IssueProgress COMPLETED, XP выдан.
        await InvokeMessageAndWaitAsync(new AiReviewIterationCompleted(
            AiReviewId: Guid.NewGuid(),
            IterationId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            IterationNumber: 1,
            Verdict: "MINOR_ISSUES",
            GitHubReviewId: 100L,
            CompletedAt: DateTimeOffset.UtcNow));

        int xpAwardsAfterApprove = 0;
        await ExecuteInDb(async db =>
        {
            xpAwardsAfterApprove = await db.XpAwards.CountAsync(
                x => x.UserId == userId && x.AwardType == XpAwardType.ISSUE_APPROVED);
        });
        Assert.Equal(1, xpAwardsAfterApprove);

        // Итерация 2: доработка (любой вердикт) по уже-APPROVED submission.
        await InvokeMessageAndWaitAsync(new AiReviewIterationCompleted(
            AiReviewId: Guid.NewGuid(),
            IterationId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            IterationNumber: 2,
            Verdict: rerunVerdict,
            GitHubReviewId: 101L,
            CompletedAt: DateTimeOffset.UtcNow));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            // Зачёт держится: статус остаётся APPROVED (даже на MAJOR), denorm обновился.
            Assert.Equal(IssueSubmissionReviewStatus.APPROVED, s.ReviewStatus);
            Assert.Equal(rerunVerdict, s.LatestAiVerdict);
            Assert.Equal(2, s.AiIterationsCount);

            // IssueProgress остаётся COMPLETED (терминал), XP не удвоился.
            IssueProgress progress = await db.IssueProgresses.FirstAsync(p => p.Id == s.IssueProgressId);
            Assert.Equal(IssueProgressStatus.COMPLETED, progress.Status);
            int xpAwards = await db.XpAwards.CountAsync(
                x => x.UserId == userId && x.AwardType == XpAwardType.ISSUE_APPROVED);
            Assert.Equal(1, xpAwards);
        });
    }

    [Fact]
    public async Task IterationCompleted_MajorIssues_RequestsChanges()
    {
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedSubmissionAsync(userId);

        // #383: MAJOR_ISSUES (задача реально не решена) → RequestChanges, студент дорабатывает.
        await InvokeMessageAndWaitAsync(new AiReviewIterationCompleted(
            AiReviewId: Guid.NewGuid(),
            IterationId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            IterationNumber: 1,
            Verdict: "MAJOR_ISSUES",
            GitHubReviewId: 200L,
            CompletedAt: DateTimeOffset.UtcNow));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.Equal(IssueSubmissionReviewStatus.CHANGES_REQUESTED, s.ReviewStatus);
            Assert.Equal("MAJOR_ISSUES", s.LatestAiVerdict);
            Assert.Equal("READY", s.AiReviewStatus);
        });
    }

    [Fact]
    public async Task IterationCompleted_OffTopic_RequestsChanges()
    {
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedSubmissionAsync(userId);

        // #383: OFF_TOPIC (PR не по теме) → RequestChanges.
        await InvokeMessageAndWaitAsync(new AiReviewIterationCompleted(
            AiReviewId: Guid.NewGuid(),
            IterationId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            IterationNumber: 1,
            Verdict: "OFF_TOPIC",
            GitHubReviewId: null,
            CompletedAt: DateTimeOffset.UtcNow));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.Equal(IssueSubmissionReviewStatus.CHANGES_REQUESTED, s.ReviewStatus);
            Assert.Equal("OFF_TOPIC", s.LatestAiVerdict);
        });
    }

    [Fact]
    public async Task IterationCompleted_FailedVerdict_MarksAiStatusFailed()
    {
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedSubmissionAsync(userId);

        await InvokeMessageAndWaitAsync(new AiReviewIterationCompleted(
            AiReviewId: Guid.NewGuid(),
            IterationId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            IterationNumber: 1,
            Verdict: string.Empty,
            GitHubReviewId: null,
            CompletedAt: DateTimeOffset.UtcNow));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.Null(s.LatestAiVerdict);
            Assert.Equal("FAILED", s.AiReviewStatus);
        });
    }

    [Fact]
    public async Task QueuedForSubmission_GatesReadyForHumanReview_AndSetsStatusQueued()
    {
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedSubmissionAsync(userId);

        await InvokeMessageAndWaitAsync(new AiReviewQueuedForSubmission(
            AiReviewId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            QueuedAt: DateTimeOffset.UtcNow));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.False(s.ReadyForHumanReview);
            Assert.Equal("QUEUED", s.AiReviewStatus);
            Assert.Equal(0, s.AiIterationsCount);
        });
    }

    [Fact]
    public async Task IterationCompleted_AfterQueue_DoesNotResetGate()
    {
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedSubmissionAsync(userId);

        await InvokeMessageAndWaitAsync(new AiReviewQueuedForSubmission(
            AiReviewId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            QueuedAt: DateTimeOffset.UtcNow));

        await InvokeMessageAndWaitAsync(new AiReviewIterationCompleted(
            AiReviewId: Guid.NewGuid(),
            IterationId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            IterationNumber: 1,
            Verdict: "LOOKS_GOOD",
            GitHubReviewId: 1L,
            CompletedAt: DateTimeOffset.UtcNow));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            // LOOKS_GOOD → авто-гейт: StartReview(AI) → Approve. Денорм-поля при этом
            // сохраняются в той же транзакции (каскад Approve проходит благодаря seed'у
            // ProjectProgress).
            Assert.Equal(IssueSubmissionReviewStatus.APPROVED, s.ReviewStatus);
            // ReadyForHumanReview авто-Approve НЕ трогает — флаг остаётся в значении,
            // выставленном Queued-событием (false). Для авто-одобренной submission он
            // больше не важен (автору в inbox она не нужна).
            Assert.False(s.ReadyForHumanReview);
            Assert.Equal("LOOKS_GOOD", s.LatestAiVerdict);
            Assert.Equal("READY", s.AiReviewStatus);
            Assert.Equal(1, s.AiIterationsCount);
        });
    }

    [Fact]
    public async Task QueuedForSubmission_OnAlreadyApproved_DoesNotDisturbCompleted()
    {
        // #725: пост-approve ре-ревью публикует AiReviewQueuedForSubmission перед прогоном.
        // Handler гейтит только PENDING — уже зачтённую (APPROVED/COMPLETED) сдачу нельзя
        // дёрнуть обратно в очередь проверки автора. Инвариант безопасности доработки.
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedSubmissionAsync(userId);

        // Доводим до APPROVED через MINOR-итерацию.
        await InvokeMessageAndWaitAsync(new AiReviewIterationCompleted(
            AiReviewId: Guid.NewGuid(),
            IterationId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            IterationNumber: 1,
            Verdict: "MINOR_ISSUES",
            GitHubReviewId: 100L,
            CompletedAt: DateTimeOffset.UtcNow));

        // Ре-ревью: снова QueuedForSubmission по уже-APPROVED submission.
        await InvokeMessageAndWaitAsync(new AiReviewQueuedForSubmission(
            AiReviewId: Guid.NewGuid(),
            SubmissionId: submissionId,
            UserId: userId,
            IssueId: Guid.NewGuid(),
            QueuedAt: DateTimeOffset.UtcNow));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            // Статус не сбит обратно в PENDING/очередь — сдача остаётся зачтённой.
            Assert.Equal(IssueSubmissionReviewStatus.APPROVED, s.ReviewStatus);
            IssueProgress progress = await db.IssueProgresses.FirstAsync(p => p.Id == s.IssueProgressId);
            Assert.Equal(IssueProgressStatus.COMPLETED, progress.Status);
        });
    }

    [Fact]
    public async Task IterationCompleted_ForUnknownSubmission_NoOp()
    {
        // Submission не сидим — handler должен залогировать и тихо выйти.
        await InvokeMessageAndWaitAsync(new AiReviewIterationCompleted(
            AiReviewId: Guid.NewGuid(),
            IterationId: Guid.NewGuid(),
            SubmissionId: Guid.NewGuid(),
            UserId: Guid.NewGuid(),
            IssueId: Guid.NewGuid(),
            IterationNumber: 1,
            Verdict: "LOOKS_GOOD",
            GitHubReviewId: null,
            CompletedAt: DateTimeOffset.UtcNow));

        // Нет assert'а — просто не должно бросить.
    }

    [Fact]
    public async Task StudentPrQuestionAsked_SetsStudentQuestionAt_AndAdvancesToLatest()
    {
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedSubmissionAsync(userId);

        // #713: студент задал вопрос в PR → денорм-timestamp появляется на сдаче.
        DateTimeOffset firstAsked = DateTimeOffset.UtcNow.AddMinutes(-5);
        await InvokeMessageAndWaitAsync(MakeStudentPrQuestion(submissionId, firstAsked));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.NotNull(s.StudentQuestionAt);
            Assert.Equal(firstAsked.UtcDateTime, s.StudentQuestionAt!.Value, TimeSpan.FromSeconds(1));
        });

        // Второй вопрос позже → timestamp двигается вперёд (бейдж отражает свежий вопрос).
        DateTimeOffset secondAsked = firstAsked.AddMinutes(3);
        await InvokeMessageAndWaitAsync(MakeStudentPrQuestion(submissionId, secondAsked));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.Equal(secondAsked.UtcDateTime, s.StudentQuestionAt!.Value, TimeSpan.FromSeconds(1));
        });

        // Ре-доставка старого event'а (out-of-order / дубль) НЕ откатывает назад — идемпотентно.
        await InvokeMessageAndWaitAsync(MakeStudentPrQuestion(submissionId, firstAsked));

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.Equal(secondAsked.UtcDateTime, s.StudentQuestionAt!.Value, TimeSpan.FromSeconds(1));
        });
    }

    [Fact]
    public async Task StudentPrQuestionAsked_ForUnknownSubmission_NoOp()
    {
        // Submission не сидим — handler должен залогировать и тихо выйти (не бросить).
        await InvokeMessageAndWaitAsync(MakeStudentPrQuestion(Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    private static StudentPrQuestionAsked MakeStudentPrQuestion(Guid submissionId, DateTimeOffset createdAt) =>
        new(
            StudentPrMessageId: Guid.NewGuid(),
            AiReviewId: Guid.NewGuid(),
            SubmissionId: submissionId,
            IssueId: Guid.NewGuid(),
            CourseId: Guid.NewGuid(),
            AuthorId: Guid.NewGuid(),
            StudentUserId: Guid.NewGuid(),
            StudentGithubLogin: "student-gh",
            StudentName: "Student",
            Body: "Почему тут ошибка?",
            RepoFullName: "test/repo",
            PullNumber: 1,
            PullRequestUrl: "https://github.com/test/repo/pull/1",
            CommentUrl: "https://github.com/test/repo/pull/1#discussion_r1",
            Path: "src/Program.cs",
            Line: 42,
            GithubCommentId: 12345L,
            CreatedAt: createdAt);

    private async Task<Guid> SeedSubmissionAsync(Guid userId)
    {
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid submissionId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(
                userId, courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            IssueProgress progress = IssueProgress.Create(enrollment.Id, projectId, Guid.NewGuid()).Value;
            progress.StartWork();
            progress.SubmitForReview();
            IssueSubmission submission = IssueSubmission.Create(
                progress.Id,
                AttemptNumber.Create(1).Value,
                IssueSubmissionPayload.Create("https://github.com/test/repo/pull/1").Value).Value;

            // Нужна для каскада авто-Approve: LOOKS_GOOD → submission.Approve() →
            // issueProgress.Approve() → UpdateProjectProgressOnIssueApproved грузит
            // ProjectProgress по (EnrollmentId, ProjectId). Без неё каскад фейлится и
            // откатывает всю транзакцию итерации (денорм-поля теряются).
            ProjectProgress projectProgress = ProjectProgress.Create(enrollment.Id, projectId, totalIssuesCount: 1).Value;

            await db.CourseEnrollments.AddAsync(enrollment);
            await db.IssueProgresses.AddAsync(progress);
            await db.ProjectProgresses.AddAsync(projectProgress);
            await db.IssueSubmissions.AddAsync(submission);
            await db.SaveChangesAsync();

            submissionId = submission.Id;
        });

        return submissionId;
    }
}
