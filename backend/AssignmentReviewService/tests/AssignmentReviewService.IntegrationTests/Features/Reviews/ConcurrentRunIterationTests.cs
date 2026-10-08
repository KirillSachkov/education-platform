using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

/// <summary>
///     Phase 13+ (#15): race condition fix — атомарный RUNNING transition
///     гарантирует, что 2 одновременных POST'а не запустят два LLM call'а.
/// </summary>
public sealed class ConcurrentRunIterationTests : AssignmentReviewServiceTestsBase
{
    public ConcurrentRunIterationTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        Factory.VcsProvider.Reset();
        Factory.AiClient.Reset();
    }

    [Fact]
    public async Task TwoConcurrentRunIteration_NoConstraintViolation_AndIterationNumbersAreUnique()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        // Manual run-iteration is author/admin-only (#16) — pipeline test acts as admin.
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.AiClient.QueueResponse("LOOKS_GOOD", "first");
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "second");

        // После #357 endpoint только публикует команду в durable outbox — exclusivity
        // навязывает уже не HTTP-стадия, а worker handler через TryAcquireRunningLock.
        // Сначала убеждаемся, что endpoint принимает оба POST'а (double-submit /
        // 2 browser tabs), затем гоняем worker handler параллельно — атомарный gate
        // на DB-уровне должен сериализовать выполнение и оставить уникальные
        // iteration_number'ы (или отдать второму review.already_running).
        Task<HttpResponseMessage> r1 = AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{reviewId}/run-iteration/", new { });
        Task<HttpResponseMessage> r2 = AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{reviewId}/run-iteration/", new { });

        HttpResponseMessage[] responses = await Task.WhenAll(r1, r2);
        foreach (HttpResponseMessage response in responses)
        {
            Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        Assert.True(responses.Any(r => r.StatusCode == HttpStatusCode.OK),
            "At least one request must succeed");

        // Параллельный invoke worker-handler'а: TryAcquireRunningLock на DB-уровне —
        // единственный реальный exclusivity-gate. Не должно быть 500 / unique-violation.
        async Task InvokeHandlerAsync()
        {
            await using AsyncServiceScope scope = Services.CreateAsyncScope();
            IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.InvokeAsync(new RunAiReviewRequested(reviewId, ModelOverride: null));
        }
        await Task.WhenAll(InvokeHandlerAsync(), InvokeHandlerAsync());

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);

            // Все iteration_number'ы уникальны — это и есть инвариант атомарного gate'а.
            int[] numbers = review.Iterations.Select(i => i.IterationNumber).ToArray();
            Assert.Equal(numbers.Distinct().Count(), numbers.Length);
        });
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
                authorId: Guid.NewGuid(),
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
}
