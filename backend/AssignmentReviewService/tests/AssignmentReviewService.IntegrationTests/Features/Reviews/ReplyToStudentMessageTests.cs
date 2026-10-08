using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Contracts.Reviews;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

/// <summary>
///     Issue #713 (1b) — автор курса отвечает студенту на реплику в PR
///     (<c>POST /assignment-review/student-messages/{id}/reply/</c>).
/// </summary>
public sealed class ReplyToStudentMessageTests : AssignmentReviewServiceTestsBase
{
    private const string REPO = "test-org/student-pr";
    private const string OWNER = "test-org";
    private const int PULL = 7;

    public ReplyToStudentMessageTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        Factory.VcsProvider.Reset();
    }

    [Fact]
    public async Task Author_ReplyToReviewComment_PostsReplyToThread_AndMarksAnswered()
    {
        Guid authorId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-author", authorId);

        (Guid reviewId, _) = await SeedReviewAsync(authorId, studentId: Guid.NewGuid());
        await SeedInstallationAsync(OWNER);
        Guid messageId = await SeedMessageAsync(reviewId, StudentPrMessageKind.REVIEW_COMMENT, 100L, inReplyTo: 99L);

        HttpResponseMessage response = await PostReplyAsync(messageId, "Спасибо за вопрос — так и задумано.");

        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Got {response.StatusCode}: {body}");

        ReplyToStudentMessageResponse? dto =
            (await response.Content.ReadFromJsonAsync<EnvelopeStub<ReplyToStudentMessageResponse>>())?.Result;
        Assert.NotNull(dto);
        Assert.Equal(messageId, dto!.MessageId);
        Assert.Equal(555L, dto.AnswerGithubCommentId); // FakeVcsProvider default reply id
        Assert.NotEmpty(dto.AnswerHtmlUrl);

        // Posted to the PR thread as a REVIEW_COMMENT reply on the student's comment id.
        (string InstallationId, string RepoFullName, int PullNumber, StudentPrMessageKind Kind, long InReplyToCommentId, string Body) posted =
            Assert.Single(Factory.VcsProvider.PostedCommentReplies);
        Assert.Equal(StudentPrMessageKind.REVIEW_COMMENT, posted.Kind);
        Assert.Equal(100L, posted.InReplyToCommentId);
        Assert.Equal(REPO, posted.RepoFullName);
        Assert.Equal(PULL, posted.PullNumber);
        Assert.Equal("Спасибо за вопрос — так и задумано.", posted.Body);

        // Answer fields persisted.
        await ExecuteInDbAsync(async db =>
        {
            StudentPrMessage m = await db.StudentPrMessages.FirstAsync(x => x.Id == messageId);
            Assert.NotNull(m.AnsweredAt);
            Assert.Equal("Спасибо за вопрос — так и задумано.", m.AnswerBody);
            Assert.Equal(555L, m.AnswerGitHubCommentId);
        });
    }

    [Fact]
    public async Task Author_ReplyToIssueComment_PostsNewPrComment_AndMarksAnswered()
    {
        Guid authorId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-author", authorId);

        (Guid reviewId, _) = await SeedReviewAsync(authorId, studentId: Guid.NewGuid());
        await SeedInstallationAsync(OWNER);
        Guid messageId = await SeedMessageAsync(reviewId, StudentPrMessageKind.ISSUE_COMMENT, 200L, inReplyTo: null);

        HttpResponseMessage response = await PostReplyAsync(messageId, "Отвечаю в ленте PR.");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        (string InstallationId, string RepoFullName, int PullNumber, StudentPrMessageKind Kind, long InReplyToCommentId, string Body) posted =
            Assert.Single(Factory.VcsProvider.PostedCommentReplies);
        Assert.Equal(StudentPrMessageKind.ISSUE_COMMENT, posted.Kind);

        await ExecuteInDbAsync(async db =>
        {
            StudentPrMessage m = await db.StudentPrMessages.FirstAsync(x => x.Id == messageId);
            Assert.NotNull(m.AnsweredAt);
            Assert.Equal("Отвечаю в ленте PR.", m.AnswerBody);
        });
    }

    [Fact]
    public async Task Admin_CanReply_ToAnyMessage()
    {
        Guid authorId = Guid.NewGuid();
        AuthenticateAs("platform-admin", Guid.NewGuid()); // not the author

        (Guid reviewId, _) = await SeedReviewAsync(authorId, studentId: Guid.NewGuid());
        await SeedInstallationAsync(OWNER);
        Guid messageId = await SeedMessageAsync(reviewId, StudentPrMessageKind.REVIEW_COMMENT, 100L, inReplyTo: 99L);

        HttpResponseMessage response = await PostReplyAsync(messageId, "Ответ администратора.");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(Factory.VcsProvider.PostedCommentReplies);
    }

    [Fact]
    public async Task ReflectsInBySubmissionDto_AfterReply()
    {
        Guid authorId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-author", authorId);

        (Guid reviewId, Guid submissionId) = await SeedReviewAsync(authorId, studentId: authorId);
        await SeedInstallationAsync(OWNER);
        Guid messageId = await SeedMessageAsync(reviewId, StudentPrMessageKind.REVIEW_COMMENT, 100L, inReplyTo: 99L);

        HttpResponseMessage reply = await PostReplyAsync(messageId, "Готовый ответ автора.");
        Assert.Equal(HttpStatusCode.OK, reply.StatusCode);

        HttpResponseMessage http = await AppHttpClient.GetAsync(
            $"/assignment-review/reviews/by-submission/{submissionId}/");
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);

        AiReviewDetailDto? detail =
            (await http.Content.ReadFromJsonAsync<EnvelopeStub<AiReviewDetailDto>>())?.Result;
        Assert.NotNull(detail);
        StudentPrMessageDto msg = Assert.Single(detail!.StudentMessages);
        Assert.NotNull(msg.AnsweredAt);
        Assert.Equal("Готовый ответ автора.", msg.AnswerBody);
    }

    [Fact]
    public async Task RepeatReply_UpdatesAnswer_AndPostsSecondReply()
    {
        Guid authorId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-author", authorId);

        (Guid reviewId, _) = await SeedReviewAsync(authorId, studentId: Guid.NewGuid());
        await SeedInstallationAsync(OWNER);
        Guid messageId = await SeedMessageAsync(reviewId, StudentPrMessageKind.REVIEW_COMMENT, 100L, inReplyTo: 99L);

        Assert.Equal(HttpStatusCode.OK, (await PostReplyAsync(messageId, "Первый ответ.")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostReplyAsync(messageId, "Второй, уточнённый ответ.")).StatusCode);

        Assert.Equal(2, Factory.VcsProvider.PostedCommentReplies.Count);

        await ExecuteInDbAsync(async db =>
        {
            StudentPrMessage m = await db.StudentPrMessages.FirstAsync(x => x.Id == messageId);
            Assert.Equal("Второй, уточнённый ответ.", m.AnswerBody);
        });
    }

    [Fact]
    public async Task NonOwner_Forbidden_AndDoesNotPost()
    {
        Guid authorId = Guid.NewGuid();
        AuthenticateAs("platform-author", Guid.NewGuid()); // different author — not the owner

        (Guid reviewId, _) = await SeedReviewAsync(authorId, studentId: Guid.NewGuid());
        await SeedInstallationAsync(OWNER);
        Guid messageId = await SeedMessageAsync(reviewId, StudentPrMessageKind.REVIEW_COMMENT, 100L, inReplyTo: 99L);

        HttpResponseMessage response = await PostReplyAsync(messageId, "Чужой ответ.");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("review.access_denied", body, StringComparison.Ordinal);
        Assert.Empty(Factory.VcsProvider.PostedCommentReplies);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyOrWhitespaceBody_BadRequest_AndDoesNotPost(string emptyBody)
    {
        Guid authorId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-author", authorId);

        (Guid reviewId, _) = await SeedReviewAsync(authorId, studentId: Guid.NewGuid());
        await SeedInstallationAsync(OWNER);
        Guid messageId = await SeedMessageAsync(reviewId, StudentPrMessageKind.REVIEW_COMMENT, 100L, inReplyTo: 99L);

        HttpResponseMessage response = await PostReplyAsync(messageId, emptyBody);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Factory.VcsProvider.PostedCommentReplies);
    }

    [Fact]
    public async Task OversizeBody_BadRequest_AndDoesNotPost()
    {
        // FIX-4: тело ответа сверх GitHub-капа (65536) отбивается валидатором → 400,
        // GitHub-вызова не происходит.
        Guid authorId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-author", authorId);

        (Guid reviewId, _) = await SeedReviewAsync(authorId, studentId: Guid.NewGuid());
        await SeedInstallationAsync(OWNER);
        Guid messageId = await SeedMessageAsync(reviewId, StudentPrMessageKind.REVIEW_COMMENT, 100L, inReplyTo: 99L);

        string tooLong = new('a', 65537);
        HttpResponseMessage response = await PostReplyAsync(messageId, tooLong);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Factory.VcsProvider.PostedCommentReplies);
    }

    [Fact]
    public async Task NonExistentMessage_NotFound()
    {
        AuthenticateAs("platform-author", AssignmentReviewServiceTestsBase.DefaultUserId);

        HttpResponseMessage response = await PostReplyAsync(Guid.NewGuid(), "Ответ в никуда.");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("review.student_message.not_found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoInstallation_ReturnsNoInstallation_AndDoesNotPost()
    {
        Guid authorId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-author", authorId);

        (Guid reviewId, _) = await SeedReviewAsync(authorId, studentId: Guid.NewGuid());
        // No installation seeded.
        Guid messageId = await SeedMessageAsync(reviewId, StudentPrMessageKind.REVIEW_COMMENT, 100L, inReplyTo: 99L);

        HttpResponseMessage response = await PostReplyAsync(messageId, "Ответ без установки.");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("review.no_installation", body, StringComparison.Ordinal);
        Assert.Empty(Factory.VcsProvider.PostedCommentReplies);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PostReplyAsync(Guid messageId, string body) =>
        AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/student-messages/{messageId}/reply/",
            new ReplyToStudentMessageRequest(body));

    private async Task<(Guid ReviewId, Guid SubmissionId)> SeedReviewAsync(Guid authorId, Guid studentId)
    {
        Guid submissionId = Guid.NewGuid();
        AiReview review = AiReview.Create(
            submissionId: submissionId,
            issueId: Guid.NewGuid(),
            userId: studentId,
            authorId: authorId,
            provider: VcsProvider.GITHUB,
            repoFullName: REPO,
            pullNumber: PULL,
            pullRequestUrl: $"https://github.com/{REPO}/pull/{PULL}");

        await ExecuteInDbAsync(async db =>
        {
            db.AiReviews.Add(review);
            await db.SaveChangesAsync();
        });
        return (review.Id, submissionId);
    }

    private async Task<Guid> SeedMessageAsync(
        Guid reviewId, StudentPrMessageKind kind, long gitHubCommentId, long? inReplyTo)
    {
        StudentPrMessage message = StudentPrMessage.Create(
            aiReviewId: reviewId,
            gitHubCommentId: gitHubCommentId,
            inReplyToGitHubId: inReplyTo,
            kind: kind,
            authorGithubLogin: "student",
            body: "Почему тут ошибка?",
            path: kind == StudentPrMessageKind.REVIEW_COMMENT ? "src/A.cs" : null,
            line: kind == StudentPrMessageKind.REVIEW_COMMENT ? 12 : null,
            commentUrl: $"https://github.com/{REPO}/pull/{PULL}#c{gitHubCommentId}",
            createdAtGithub: DateTimeOffset.UtcNow.AddMinutes(-5),
            ingestedAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        await ExecuteInDbAsync(async db =>
        {
            db.StudentPrMessages.Add(message);
            await db.SaveChangesAsync();
        });
        return message.Id;
    }

    private async Task SeedInstallationAsync(string ownerLogin)
    {
        await ExecuteInDbAsync(async db =>
        {
            VcsInstallation installation = VcsInstallation.Create(
                VcsProvider.GITHUB,
                installationId: "999",
                ownerType: VcsInstallationOwnerType.ORG,
                ownerLogin: ownerLogin,
                ownerExternalId: "555",
                linkedUserId: AssignmentReviewServiceTestsBase.DefaultUserId,
                repoSelections: RepoSelections.AllRepos());
            db.VcsInstallations.Add(installation);
            await db.SaveChangesAsync();
        });
    }

    private sealed class EnvelopeStub<T>
    {
        public T? Result { get; set; }
    }
}
