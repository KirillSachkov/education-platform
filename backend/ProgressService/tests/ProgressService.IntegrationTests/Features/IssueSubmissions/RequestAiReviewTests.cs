using System.Net;
using System.Net.Http.Json;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.IntegrationTests.Infrastructure;
using Shared.Messaging.IntegrationEvents.Progress.Events;

namespace ProgressService.IntegrationTests.Features.IssueSubmissions;

/// <summary>
///     #383 «Запустить AI принудительно»: автор/админ форсит AI-проверку для submission
///     без AiReview. Endpoint ре-публикует <see cref="IssueSubmissionAwaitingReview"/> —
///     идемпотентный ARS-handler создаёт+запускает AiReview если его нет.
///     L2 — проверяем что событие реально публикуется с тем же payload'ом, что и create-time.
/// </summary>
public sealed class RequestAiReviewTests : ProgressServiceTestsBase
{
    public RequestAiReviewTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Author_ForceRequest_RepublishesAwaitingReview()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        const string prUrl = "https://github.com/owner/repo/pull/42";

        // ECS должен резолвить AuthorId курса (как в create-time publisher).
        EducationContentClient.AddCourse(courseId, authorId);
        Guid submissionId = await SeedSubmissionAsync(userId, authorId, courseId, issueId, prUrl);

        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        NoOpOutboxService.Reset();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/request-ai-review/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IssueSubmissionAwaitingReview published = NoOpOutboxService.Published
            .OfType<IssueSubmissionAwaitingReview>()
            .Single();
        Assert.Equal(submissionId, published.SubmissionId);
        Assert.Equal(userId, published.StudentUserId);
        Assert.Equal(authorId, published.AuthorId);
        Assert.Equal(issueId, published.IssueId);
        Assert.Equal(courseId, published.CourseId);
        Assert.Equal(prUrl, published.Payload);
    }

    [Fact]
    public async Task Admin_CanForceRequest()
    {
        Guid userId = Guid.NewGuid();
        Guid authorId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        EducationContentClient.AddCourse(courseId, authorId);
        Guid submissionId = await SeedSubmissionAsync(userId, authorId, courseId, Guid.NewGuid(),
            "https://github.com/owner/repo/pull/7");

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/request-ai-review/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Participant_Forbidden()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        EducationContentClient.AddCourse(courseId, Guid.NewGuid());
        Guid submissionId = await SeedSubmissionAsync(userId, Guid.NewGuid(), courseId, Guid.NewGuid(),
            "https://github.com/owner/repo/pull/3");

        // platform-participant не имеет progress.manage.
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/request-ai-review/", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnknownSubmission_NotFound()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-moderator");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{Guid.NewGuid()}/request-ai-review/", new { });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> SeedSubmissionAsync(
        Guid userId, Guid authorId, Guid courseId, Guid issueId, string prUrl)
    {
        Guid submissionId = Guid.Empty;
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
                IssueSubmissionPayload.Create(prUrl).Value).Value;

            await db.CourseEnrollments.AddAsync(enrollment);
            await db.IssueProgresses.AddAsync(progress);
            await db.IssueSubmissions.AddAsync(submission);
            await db.SaveChangesAsync();

            submissionId = submission.Id;
        });
        return submissionId;
    }
}
