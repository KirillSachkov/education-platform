using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.IssueSubmissions;
using ProgressService.Domain.Issues;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.IssueSubmissions;

public sealed class FinalizeSubmissionTests : ProgressServiceTestsBase
{
    public FinalizeSubmissionTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task Owner_CanFinalize_AndFlagFlipsToTrue()
    {
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedGatedSubmissionAsync(userId);
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/finalize/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.True(s.ReadyForHumanReview);
        });
    }

    [Fact]
    public async Task Idempotent_SecondCallSucceedsAndKeepsTrue()
    {
        Guid userId = Guid.NewGuid();
        Guid submissionId = await SeedGatedSubmissionAsync(userId);
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage first = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/finalize/", new { });
        HttpResponseMessage second = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/finalize/", new { });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task NonOwner_Returns403_ish()
    {
        Guid ownerId = Guid.NewGuid();
        Guid attackerId = Guid.NewGuid();
        Guid submissionId = await SeedGatedSubmissionAsync(ownerId);
        AuthenticateAs(attackerId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/finalize/", new { });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            IssueSubmission s = await db.IssueSubmissions.FirstAsync(x => x.Id == submissionId);
            Assert.False(s.ReadyForHumanReview);
        });
    }

    [Fact]
    public async Task Admin_CanFinalizeAnyone()
    {
        Guid ownerId = Guid.NewGuid();
        Guid submissionId = await SeedGatedSubmissionAsync(ownerId);
        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/finalize/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        Guid submissionId = await SeedGatedSubmissionAsync(Guid.NewGuid());
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/progress/submissions/{submissionId}/finalize/", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<Guid> SeedGatedSubmissionAsync(Guid userId)
    {
        Guid submissionId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(
                userId, Guid.NewGuid(), Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            IssueProgress progress = IssueProgress.Create(enrollment.Id, Guid.NewGuid(), Guid.NewGuid()).Value;
            progress.StartWork();
            progress.SubmitForReview();

            // autoFinalize=false → ReadyForHumanReview=false (gated by AI).
            IssueSubmission submission = IssueSubmission.Create(
                progress.Id,
                AttemptNumber.Create(1).Value,
                IssueSubmissionPayload.Create("https://github.com/owner/repo/pull/1").Value,
                autoFinalize: false).Value;

            await db.CourseEnrollments.AddAsync(enrollment);
            await db.IssueProgresses.AddAsync(progress);
            await db.IssueSubmissions.AddAsync(submission);
            await db.SaveChangesAsync();

            submissionId = submission.Id;
        });
        return submissionId;
    }
}
