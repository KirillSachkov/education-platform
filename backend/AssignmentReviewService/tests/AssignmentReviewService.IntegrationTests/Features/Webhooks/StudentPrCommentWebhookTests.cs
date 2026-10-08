using System.Net;
using System.Security.Cryptography;
using System.Text;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using AuthService.Contracts;
using Microsoft.EntityFrameworkCore;
using Shared.Messaging.IntegrationEvents.AssignmentReview;

namespace AssignmentReviewService.IntegrationTests.Features.Webhooks;

/// <summary>
///     #713 — обратный канал AI-ревью: платформа слышит комментарии студента в его PR через
///     webhook'и <c>pull_request_review_comment</c> / <c>issue_comment</c>, кладёт в
///     <c>StudentPrMessage</c> и публикует <see cref="StudentPrQuestionAsked"/>.
/// </summary>
public sealed class StudentPrCommentWebhookTests : AssignmentReviewServiceTestsBase
{
    private const string StudentLogin = "vladimLi";
    private const string RepoFullName = "vladimLi/directory";
    private const int PullNumber = 8;

    public StudentPrCommentWebhookTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task ReviewComment_FromPrOwner_CreatesMessage_And_PublishesEvent()
    {
        AiReview review = await SeedReviewAsync();

        // AuthService резолвит имя студента для StudentName в событии.
        Guid studentUserId = review.UserId;
        Factory.AuthClient.UsersByIdsHandler = _ =>
            [new AuthUserLookupDto(studentUserId, "Владимир Ли", "vladimli", "v@example.com", null)];

        const long commentId = 555001L;
        const long inReplyToId = 444000L;
        string body = ReviewCommentBody(
            action: "created",
            commentId: commentId,
            inReplyToId: inReplyToId,
            authorLogin: StudentLogin,
            prOwnerLogin: StudentLogin,
            commentBody: "если сделать как советуешь — в БД ничего не пишется",
            path: "src/Foo.cs",
            line: 26,
            htmlUrl: "https://github.com/vladimLi/directory/pull/8#discussion_r555001",
            repoFullName: RepoFullName,
            pullNumber: PullNumber);

        HttpResponseMessage response = await PostWebhookAsync(body, "pull_request_review_comment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Строка создана с ожидаемыми полями.
        StudentPrMessage stored = await ExecuteInDbAsync(async db =>
            await db.StudentPrMessages.SingleAsync(m => m.GitHubCommentId == commentId));
        Assert.Equal(review.Id, stored.AiReviewId);
        Assert.Equal(StudentPrMessageKind.REVIEW_COMMENT, stored.Kind);
        Assert.Equal(inReplyToId, stored.InReplyToGitHubId);
        Assert.Equal(StudentLogin, stored.AuthorGithubLogin);
        Assert.Equal("src/Foo.cs", stored.Path);
        Assert.Equal(26, stored.Line);
        Assert.Contains("в БД ничего не пишется", stored.Body, StringComparison.Ordinal);

        // Событие опубликовано ровно один раз со всем контрактом.
        StudentPrQuestionAsked published = OutboxCollector.OfType<StudentPrQuestionAsked>().Single();
        Assert.Equal(stored.Id, published.StudentPrMessageId);
        Assert.Equal(review.Id, published.AiReviewId);
        Assert.Equal(review.SubmissionId, published.SubmissionId);
        Assert.Equal(review.IssueId, published.IssueId);
        Assert.Null(published.CourseId);
        Assert.Equal(review.AuthorId, published.AuthorId);
        Assert.Equal(studentUserId, published.StudentUserId);
        Assert.Equal(StudentLogin, published.StudentGithubLogin);
        Assert.Equal("Владимир Ли", published.StudentName);
        Assert.Equal(review.RepoFullName, published.RepoFullName);
        Assert.Equal(review.PullNumber, published.PullNumber);
        Assert.Equal(review.PullRequestUrl, published.PullRequestUrl);
        Assert.Equal("https://github.com/vladimLi/directory/pull/8#discussion_r555001", published.CommentUrl);
        Assert.Equal("src/Foo.cs", published.Path);
        Assert.Equal(26, published.Line);
        Assert.Equal(commentId, published.GithubCommentId);
    }

    [Fact]
    public async Task ReviewComment_RepoCasingDiffers_StillMatchesReview()
    {
        // AiReview хранит casing из submission-URL студента; webhook несёт каноничный GitHub casing.
        AiReview review = await SeedReviewAsync(repoFullName: "VladimLi/Directory");

        const long commentId = 555010L;
        string body = ReviewCommentBody(
            action: "created",
            commentId: commentId,
            inReplyToId: null,
            authorLogin: "vladimli",
            prOwnerLogin: "vladimli",
            commentBody: "вопрос по строке",
            path: "a.cs",
            line: 1,
            htmlUrl: "https://github.com/vladimli/directory/pull/8",
            repoFullName: "vladimli/directory",
            pullNumber: PullNumber);

        HttpResponseMessage response = await PostWebhookAsync(body, "pull_request_review_comment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        StudentPrMessage stored = await ExecuteInDbAsync(async db =>
            await db.StudentPrMessages.SingleAsync(m => m.GitHubCommentId == commentId));
        Assert.Equal(review.Id, stored.AiReviewId);
        Assert.Single(OutboxCollector.OfType<StudentPrQuestionAsked>());
    }

    [Fact]
    public async Task ReviewComment_FromBot_Ignored()
    {
        await SeedReviewAsync();

        const long commentId = 555002L;
        string body = ReviewCommentBody(
            action: "created",
            commentId: commentId,
            inReplyToId: null,
            authorLogin: "sachkov-learn-reviewer[bot]",
            prOwnerLogin: StudentLogin,
            commentBody: "бот пишет ревью",
            path: "src/Foo.cs",
            line: 26,
            htmlUrl: "https://github.com/vladimLi/directory/pull/8#discussion_r555002",
            repoFullName: RepoFullName,
            pullNumber: PullNumber);

        HttpResponseMessage response = await PostWebhookAsync(body, "pull_request_review_comment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNoMessageAsync(commentId);
        Assert.Empty(OutboxCollector.OfType<StudentPrQuestionAsked>());
    }

    [Fact]
    public async Task ReviewComment_FromStranger_Ignored()
    {
        await SeedReviewAsync();

        const long commentId = 555003L;
        string body = ReviewCommentBody(
            action: "created",
            commentId: commentId,
            inReplyToId: null,
            authorLogin: "random-passerby",
            prOwnerLogin: StudentLogin,
            commentBody: "мимокрокодил",
            path: "src/Foo.cs",
            line: 26,
            htmlUrl: "https://github.com/vladimLi/directory/pull/8#discussion_r555003",
            repoFullName: RepoFullName,
            pullNumber: PullNumber);

        HttpResponseMessage response = await PostWebhookAsync(body, "pull_request_review_comment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNoMessageAsync(commentId);
        Assert.Empty(OutboxCollector.OfType<StudentPrQuestionAsked>());
    }

    [Fact]
    public async Task IssueComment_OnPr_FromOwner_CreatesMessage_And_PublishesEvent()
    {
        AiReview review = await SeedReviewAsync();

        const long commentId = 666001L;
        string body = IssueCommentBody(
            action: "created",
            commentId: commentId,
            authorLogin: StudentLogin,
            issueOwnerLogin: StudentLogin,
            commentBody: "не понял, как это исправить",
            htmlUrl: "https://github.com/vladimLi/directory/pull/8#issuecomment-666001",
            repoFullName: RepoFullName,
            issueNumber: PullNumber,
            isPullRequest: true);

        HttpResponseMessage response = await PostWebhookAsync(body, "issue_comment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        StudentPrMessage stored = await ExecuteInDbAsync(async db =>
            await db.StudentPrMessages.SingleAsync(m => m.GitHubCommentId == commentId));
        Assert.Equal(review.Id, stored.AiReviewId);
        Assert.Equal(StudentPrMessageKind.ISSUE_COMMENT, stored.Kind);
        Assert.Null(stored.InReplyToGitHubId);
        Assert.Null(stored.Path);
        Assert.Null(stored.Line);

        StudentPrQuestionAsked published = OutboxCollector.OfType<StudentPrQuestionAsked>().Single();
        Assert.Equal(stored.Id, published.StudentPrMessageId);
        Assert.Null(published.Path);
        Assert.Null(published.Line);
        // Имя не сконфигурировано в AuthService-fake → событие уходит без имени, но публикуется.
        Assert.Null(published.StudentName);
    }

    [Fact]
    public async Task IssueComment_WithoutPullRequest_Ignored()
    {
        await SeedReviewAsync();

        const long commentId = 666002L;
        string body = IssueCommentBody(
            action: "created",
            commentId: commentId,
            authorLogin: StudentLogin,
            issueOwnerLogin: StudentLogin,
            commentBody: "коммент к обычному issue",
            htmlUrl: "https://github.com/vladimLi/directory/issues/8#issuecomment-666002",
            repoFullName: RepoFullName,
            issueNumber: PullNumber,
            isPullRequest: false);

        HttpResponseMessage response = await PostWebhookAsync(body, "issue_comment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNoMessageAsync(commentId);
        Assert.Empty(OutboxCollector.OfType<StudentPrQuestionAsked>());
    }

    [Fact]
    public async Task DuplicateDelivery_SameCommentId_Idempotent()
    {
        await SeedReviewAsync();

        const long commentId = 555004L;
        string body = ReviewCommentBody(
            action: "created",
            commentId: commentId,
            inReplyToId: null,
            authorLogin: StudentLogin,
            prOwnerLogin: StudentLogin,
            commentBody: "повторная доставка",
            path: "src/Foo.cs",
            line: 10,
            htmlUrl: "https://github.com/vladimLi/directory/pull/8#discussion_r555004",
            repoFullName: RepoFullName,
            pullNumber: PullNumber);

        HttpResponseMessage first = await PostWebhookAsync(body, "pull_request_review_comment");
        HttpResponseMessage second = await PostWebhookAsync(body, "pull_request_review_comment");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        int rows = await ExecuteInDbAsync(async db =>
            await db.StudentPrMessages.CountAsync(m => m.GitHubCommentId == commentId));
        Assert.Equal(1, rows);
        Assert.Single(OutboxCollector.OfType<StudentPrQuestionAsked>());
    }

    [Fact]
    public async Task ReviewComment_OnPr_WithoutAiReview_NoOp()
    {
        // AiReview не создан для этого PR → это не отслеживаемая сдача.
        const long commentId = 555005L;
        string body = ReviewCommentBody(
            action: "created",
            commentId: commentId,
            inReplyToId: null,
            authorLogin: StudentLogin,
            prOwnerLogin: StudentLogin,
            commentBody: "нет ревью на этот PR",
            path: "src/Foo.cs",
            line: 5,
            htmlUrl: "https://github.com/vladimLi/directory/pull/8#discussion_r555005",
            repoFullName: RepoFullName,
            pullNumber: PullNumber);

        HttpResponseMessage response = await PostWebhookAsync(body, "pull_request_review_comment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertNoMessageAsync(commentId);
        Assert.Empty(OutboxCollector.OfType<StudentPrQuestionAsked>());
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<AiReview> SeedReviewAsync(string repoFullName = RepoFullName)
    {
        AiReview review = AiReview.Create(
            submissionId: Guid.NewGuid(),
            issueId: Guid.NewGuid(),
            userId: Guid.NewGuid(),
            authorId: Guid.NewGuid(),
            provider: VcsProvider.GITHUB,
            repoFullName: repoFullName,
            pullNumber: PullNumber,
            pullRequestUrl: $"https://github.com/{repoFullName}/pull/{PullNumber}");

        await ExecuteInDbAsync(async db =>
        {
            db.AiReviews.Add(review);
            await db.SaveChangesAsync();
        });
        return review;
    }

    private async Task AssertNoMessageAsync(long commentId) =>
        await ExecuteInDbAsync(async db =>
            Assert.False(await db.StudentPrMessages.AnyAsync(m => m.GitHubCommentId == commentId)));

    private async Task<HttpResponseMessage> PostWebhookAsync(string body, string eventName)
    {
        HttpRequestMessage req = new(HttpMethod.Post, "/assignment-review/webhooks/github")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Hub-Signature-256", ComputeSignature(body, IntegrationTestsWebFactory.TestWebhookSecret));
        req.Headers.Add("X-GitHub-Event", eventName);
        return await AppHttpClient.SendAsync(req);
    }

    private static string ReviewCommentBody(
        string action,
        long commentId,
        long? inReplyToId,
        string authorLogin,
        string prOwnerLogin,
        string commentBody,
        string path,
        int line,
        string htmlUrl,
        string repoFullName,
        int pullNumber)
    {
        string inReplyToJson = inReplyToId is null
            ? "null"
            : inReplyToId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return $$"""
            {
              "action": "{{action}}",
              "comment": {
                "id": {{commentId}},
                "in_reply_to_id": {{inReplyToJson}},
                "user": { "login": "{{authorLogin}}" },
                "body": {{JsonString(commentBody)}},
                "path": "{{path}}",
                "line": {{line}},
                "html_url": "{{htmlUrl}}",
                "created_at": "2026-07-06T10:00:00Z"
              },
              "pull_request": {
                "number": {{pullNumber}},
                "user": { "login": "{{prOwnerLogin}}" }
              },
              "repository": { "full_name": "{{repoFullName}}" }
            }
            """;
    }

    private static string IssueCommentBody(
        string action,
        long commentId,
        string authorLogin,
        string issueOwnerLogin,
        string commentBody,
        string htmlUrl,
        string repoFullName,
        int issueNumber,
        bool isPullRequest)
    {
        string pullRequestJson = isPullRequest
            ? $$"""{ "html_url": "{{htmlUrl}}" }"""
            : "null";

        return $$"""
            {
              "action": "{{action}}",
              "comment": {
                "id": {{commentId}},
                "user": { "login": "{{authorLogin}}" },
                "body": {{JsonString(commentBody)}},
                "html_url": "{{htmlUrl}}",
                "created_at": "2026-07-06T11:00:00Z"
              },
              "issue": {
                "number": {{issueNumber}},
                "user": { "login": "{{issueOwnerLogin}}" },
                "pull_request": {{pullRequestJson}}
              },
              "repository": { "full_name": "{{repoFullName}}" }
            }
            """;
    }

    private static string JsonString(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value);

    private static string ComputeSignature(string body, string secret)
    {
        using HMACSHA256 hmac = new(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}
