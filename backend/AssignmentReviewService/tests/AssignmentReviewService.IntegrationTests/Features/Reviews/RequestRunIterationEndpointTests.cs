using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

/// <summary>
///     L2-тесты на async endpoint <c>POST /run-iteration/</c> (#357). Endpoint —
///     thin shell: после fast-fail authz/state-проверок публикует
///     <see cref="RunAiReviewRequested"/> в durable outbox и сразу возвращает 200.
///     Проверяем что:
///     (a) endpoint реально публикует event'у (outbox-collector ловит);
///     (b) при концептуальных failures (отсутствие review, чужой ownership)
///         event'а в outbox НЕ публикуется — fast-fail отбрасывает запрос.
/// </summary>
public sealed class RequestRunIterationEndpointTests : AssignmentReviewServiceTestsBase
{
    public RequestRunIterationEndpointTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        Factory.VcsProvider.Reset();
        Factory.AiClient.Reset();
    }

    [Fact]
    public async Task Endpoint_Accepts_PublishesRunAiReviewRequested()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{reviewId}/run-iteration/", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RunAiReviewRequested published = OutboxCollector.OfType<RunAiReviewRequested>().Single();
        Assert.Equal(reviewId, published.AiReviewId);
        Assert.Null(published.ModelOverride);
    }

    [Fact]
    public async Task Endpoint_NotOwner_403_DoesNotPublish()
    {
        Guid ownerUserId = Guid.NewGuid();
        Guid otherUserId = Guid.NewGuid();
        AuthenticateAs("platform-participant", otherUserId);

        Guid reviewId = await SeedReviewAsync(ownerUserId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{reviewId}/run-iteration/", new { });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    [Fact]
    public async Task Endpoint_ReviewMissing_DoesNotPublish()
    {
        AuthenticateAs("platform-admin");
        Guid bogusId = Guid.NewGuid();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{bogusId}/run-iteration/", new { });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    private async Task<Guid> SeedReviewAsync(Guid userId)
    {
        Guid reviewId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            AiReview review = AiReview.Create(
                submissionId: Guid.NewGuid(),
                issueId: Guid.NewGuid(),
                userId: userId,
                authorId: AssignmentReviewServiceTestsBase.DefaultUserId,
                provider: VcsProvider.GITHUB,
                repoFullName: "test-org/student-pr",
                pullNumber: 7,
                pullRequestUrl: "https://github.com/test-org/student-pr/pull/7");
            db.Entry(review).Property("Id").CurrentValue = reviewId;
            db.AiReviews.Add(review);
            await db.SaveChangesAsync();
        });
        return reviewId;
    }
}
