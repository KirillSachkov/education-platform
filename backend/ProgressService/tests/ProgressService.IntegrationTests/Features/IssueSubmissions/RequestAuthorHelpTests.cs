using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace ProgressService.IntegrationTests.Features.IssueSubmissions;

/// <summary>
///     #383 «Позвать автора»: студент явно подключает автора курса к проверке.
///     L1 — доменная идемпотентность <c>RequestAuthorHelp</c>.
///     L2 — endpoint выставляет timestamp + публикует <see cref="IssueSubmissionAuthorHelpRequested"/>
///     ровно один раз (через <see cref="NoOpOutboxService"/>).
/// </summary>
public sealed class RequestAuthorHelpTests : ProgressServiceTestsBase
{
    public RequestAuthorHelpTests(IntegrationTestsWebFactory factory) : base(factory) { }

    // ---- L1: domain idempotency -------------------------------------------------

    [Fact]
    public void RequestAuthorHelp_FirstCall_SetsTimestamp_AndSignalsFirstRequest()
    {
        IssueSubmission submission = NewSubmission();

        DateTime now = DateTime.UtcNow;
        var result = submission.RequestAuthorHelp(now, null, out bool firstRequest);

        Assert.True(result.IsSuccess);
        Assert.True(firstRequest);
        Assert.Equal(now, submission.AuthorHelpRequestedAt);
    }

    [Fact]
    public void RequestAuthorHelp_StoresTrimmedMessage_AndWhitespaceBecomesNull()
    {
        IssueSubmission withMessage = NewSubmission();
        withMessage.RequestAuthorHelp(DateTime.UtcNow, "  нужна помощь с тестами  ", out _);
        Assert.Equal("нужна помощь с тестами", withMessage.AuthorHelpMessage);

        IssueSubmission blank = NewSubmission();
        blank.RequestAuthorHelp(DateTime.UtcNow, "   ", out _);
        Assert.Null(blank.AuthorHelpMessage);
    }

    [Fact]
    public void RequestAuthorHelp_SecondCall_IsNoOp_AndKeepsOriginalTimestampAndMessage()
    {
        IssueSubmission submission = NewSubmission();

        DateTime first = DateTime.UtcNow.AddMinutes(-5);
        submission.RequestAuthorHelp(first, "первый текст", out _);

        var second = submission.RequestAuthorHelp(DateTime.UtcNow, "второй текст", out bool firstRequest);

        Assert.True(second.IsSuccess);
        Assert.False(firstRequest);
        Assert.Equal(first, submission.AuthorHelpRequestedAt);
        // Сообщение фиксируется на первом переходе (first-write-wins) — повтор не перетирает.
        Assert.Equal("первый текст", submission.AuthorHelpMessage);
    }

    private static IssueSubmission NewSubmission() =>
        IssueSubmission.Create(
            Guid.NewGuid(),
            AttemptNumber.Create(1).Value,
            IssueSubmissionPayload.Create("https://github.com/owner/repo/pull/1").Value).Value;

    // ---- L2: endpoint publishes event -------------------------------------------

    [Fact]
    public async Task Owner_RequestsHelp_SetsTimestamp_AndPublishesEvent()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        (Guid submissionId, Guid issueProgressId) = await SeedSubmissionAsync(userId, authorId, courseId, issueId);
        AuthenticateAs(userId, "platform-participant");

        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/request-author-help/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.NotNull(s.AuthorHelpRequestedAt);
        });

        IssueSubmissionAuthorHelpRequested published = NoOpOutboxService.Published
            .OfType<IssueSubmissionAuthorHelpRequested>()
            .Single();
        Assert.Equal(submissionId, published.SubmissionId);
        Assert.Equal(issueProgressId, published.IssueProgressId);
        Assert.Equal(userId, published.StudentUserId);
        Assert.Equal(authorId, published.AuthorId);
        Assert.Equal(issueId, published.IssueId);
        Assert.Equal(courseId, published.CourseId);
    }

    [Fact]
    public async Task Owner_RequestsHelp_WithMessage_PersistsAndPublishesMessage()
    {
        Guid userId = Guid.NewGuid();
        (Guid submissionId, _) = await SeedSubmissionAsync(userId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        AuthenticateAs(userId, "platform-participant");

        NoOpOutboxService.Reset();

        const string message = "Не проходит тест на пагинацию, посмотрите мой подход.";
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/request-author-help/", new { message });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.Equal(message, s.AuthorHelpMessage);
        });

        IssueSubmissionAuthorHelpRequested published = NoOpOutboxService.Published
            .OfType<IssueSubmissionAuthorHelpRequested>()
            .Single();
        Assert.Equal(message, published.Message);
    }

    [Fact]
    public async Task SecondCall_Idempotent_DoesNotPublishTwice()
    {
        Guid userId = Guid.NewGuid();
        (Guid submissionId, _) = await SeedSubmissionAsync(userId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        AuthenticateAs(userId, "platform-participant");

        NoOpOutboxService.Reset();

        HttpResponseMessage first = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/request-author-help/", new { });
        HttpResponseMessage second = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/request-author-help/", new { });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // Событие публикуется только на первом переходе.
        Assert.Single(NoOpOutboxService.Published.OfType<IssueSubmissionAuthorHelpRequested>());
    }

    [Fact]
    public async Task NonOwner_Forbidden_AndNoEvent()
    {
        Guid ownerId = Guid.NewGuid();
        Guid attackerId = Guid.NewGuid();
        (Guid submissionId, _) = await SeedSubmissionAsync(ownerId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        AuthenticateAs(attackerId, "platform-participant");

        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/request-author-help/", new { });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(NoOpOutboxService.Published.OfType<IssueSubmissionAuthorHelpRequested>());

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.Null(s.AuthorHelpRequestedAt);
        });
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        (Guid submissionId, _) = await SeedSubmissionAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/request-author-help/", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<(Guid SubmissionId, Guid IssueProgressId)> SeedSubmissionAsync(
        Guid userId, Guid authorId, Guid courseId, Guid issueId)
    {
        Guid submissionId = Guid.Empty;
        Guid issueProgressId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(
                userId, courseId, authorId, EnrollmentSource.ENGAGEMENT).Value;
            IssueProgress progress = IssueProgress.Create(enrollment.Id, Guid.NewGuid(), issueId).Value;
            progress.StartWork();
            progress.SubmitForReview();
            IssueSubmission submission = IssueSubmission.Create(
                progress.Id,
                AttemptNumber.Create(1).Value,
                IssueSubmissionPayload.Create("https://github.com/owner/repo/pull/1").Value).Value;

            await db.CourseEnrollments.AddAsync(enrollment);
            await db.IssueProgresses.AddAsync(progress);
            await db.IssueSubmissions.AddAsync(submission);
            await db.SaveChangesAsync();

            submissionId = submission.Id;
            issueProgressId = progress.Id;
        });
        return (submissionId, issueProgressId);
    }
}
