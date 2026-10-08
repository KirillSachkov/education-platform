using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Contracts.Reviews;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

public sealed class GetReviewBySubmissionTests : AssignmentReviewServiceTestsBase
{
    public GetReviewBySubmissionTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Owner_GetsReview()
    {
        Guid ownerId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-participant", ownerId);

        Guid submissionId = Guid.NewGuid();
        await SeedReviewAsync(submissionId, ownerId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/assignment-review/reviews/by-submission/{submissionId}/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonOwner_Forbidden()
    {
        Guid ownerId = Guid.NewGuid();
        Guid attackerId = Guid.NewGuid();
        AuthenticateAs("platform-participant", attackerId);

        Guid submissionId = Guid.NewGuid();
        await SeedReviewAsync(submissionId, ownerId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/assignment-review/reviews/by-submission/{submissionId}/");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("review.access_denied", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_CanReadAnyReview()
    {
        Guid ownerId = Guid.NewGuid();
        AuthenticateAs("platform-admin", Guid.NewGuid());

        Guid submissionId = Guid.NewGuid();
        await SeedReviewAsync(submissionId, ownerId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/assignment-review/reviews/by-submission/{submissionId}/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NotFound_When_NoReview_ExistsForSubmission()
    {
        AuthenticateAs("platform-participant", Guid.NewGuid());
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/assignment-review/reviews/by-submission/{Guid.NewGuid()}/");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("review.not_found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StudentMessages_ReturnedInCreatedAtOrder_WithAnswerFields()
    {
        Guid ownerId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-participant", ownerId);

        Guid submissionId = Guid.NewGuid();
        Guid reviewId = await SeedReviewReturningIdAsync(submissionId, ownerId);

        DateTimeOffset t0 = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);
        // Вставляем в обратном порядке — endpoint должен вернуть по возрастанию createdAt.
        await ExecuteInDbAsync(async db =>
        {
            StudentPrMessage later = StudentPrMessage.Create(
                aiReviewId: reviewId,
                gitHubCommentId: 200L,
                inReplyToGitHubId: null,
                kind: StudentPrMessageKind.ISSUE_COMMENT,
                authorGithubLogin: "student",
                body: "второй по времени",
                path: null,
                line: null,
                commentUrl: "https://github.com/owner/repo/pull/1#issuecomment-200",
                createdAtGithub: t0.AddMinutes(5),
                ingestedAt: t0.AddMinutes(6));

            StudentPrMessage earlier = StudentPrMessage.Create(
                aiReviewId: reviewId,
                gitHubCommentId: 100L,
                inReplyToGitHubId: 99L,
                kind: StudentPrMessageKind.REVIEW_COMMENT,
                authorGithubLogin: "student",
                body: "первый по времени",
                path: "src/A.cs",
                line: 12,
                commentUrl: "https://github.com/owner/repo/pull/1#discussion_r100",
                createdAtGithub: t0,
                ingestedAt: t0.AddMinutes(1));
            earlier.MarkAnswered("ответ автора", answerGitHubCommentId: 300L, answeredAt: t0.AddMinutes(10));

            db.StudentPrMessages.Add(later);
            db.StudentPrMessages.Add(earlier);
            await db.SaveChangesAsync();
        });

        HttpResponseMessage http = await AppHttpClient.GetAsync(
            $"/assignment-review/reviews/by-submission/{submissionId}/");
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AiReviewDetailDto? dto = (await http.Content.ReadFromJsonAsync<EnvelopeStub<AiReviewDetailDto>>())?.Result;

        Assert.NotNull(dto);
        Assert.Equal(2, dto!.StudentMessages.Count);

        StudentPrMessageDto first = dto.StudentMessages[0];
        Assert.Equal(100L, first.GithubCommentId);
        Assert.Equal(99L, first.InReplyToGithubId);
        Assert.Equal("student", first.AuthorGithubLogin);
        Assert.Equal("src/A.cs", first.Path);
        Assert.Equal(12, first.Line);
        Assert.Equal("первый по времени", first.Body);
        Assert.NotNull(first.AnsweredAt);
        Assert.Equal("ответ автора", first.AnswerBody);

        StudentPrMessageDto second = dto.StudentMessages[1];
        Assert.Equal(200L, second.GithubCommentId);
        Assert.Null(second.InReplyToGithubId);
        Assert.Null(second.Path);
        Assert.Null(second.Line);
        Assert.Null(second.AnsweredAt);
        Assert.Null(second.AnswerBody);
    }

    [Fact]
    public async Task StudentMessages_EmptyArray_WhenNone()
    {
        Guid ownerId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-participant", ownerId);

        Guid submissionId = Guid.NewGuid();
        await SeedReviewAsync(submissionId, ownerId);

        HttpResponseMessage http = await AppHttpClient.GetAsync(
            $"/assignment-review/reviews/by-submission/{submissionId}/");
        Assert.Equal(HttpStatusCode.OK, http.StatusCode);
        AiReviewDetailDto? dto = (await http.Content.ReadFromJsonAsync<EnvelopeStub<AiReviewDetailDto>>())?.Result;

        Assert.NotNull(dto);
        Assert.Empty(dto!.StudentMessages);
    }

    private sealed class EnvelopeStub<T>
    {
        public T? Result { get; set; }
    }

    private async Task SeedReviewAsync(Guid submissionId, Guid userId) =>
        await SeedReviewReturningIdAsync(submissionId, userId);

    private async Task<Guid> SeedReviewReturningIdAsync(Guid submissionId, Guid userId)
    {
        AiReview review = AiReview.Create(
            submissionId: submissionId,
            issueId: Guid.NewGuid(),
            userId: userId,
            authorId: Guid.NewGuid(),
            provider: VcsProvider.GITHUB,
            repoFullName: "owner/repo",
            pullNumber: 1,
            pullRequestUrl: "https://github.com/owner/repo/pull/1");

        await ExecuteInDbAsync(async db =>
        {
            db.AiReviews.Add(review);
            await db.SaveChangesAsync();
        });
        return review.Id;
    }
}
