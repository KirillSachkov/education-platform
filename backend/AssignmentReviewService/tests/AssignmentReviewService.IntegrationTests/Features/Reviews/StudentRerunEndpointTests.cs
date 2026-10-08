using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Domain.AiSettings;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

/// <summary>
///     L2-тесты на студенческий endpoint <c>POST /reviews/{id}/student-rerun/</c> (#725,
///     гейт переписан в #976): доработка после AI-approve с замечаниями.
///     Гейт: владелец (<c>review.UserId</c>) + есть COMPLETED-итерация + не RUNNING.
///     Проверяем happy-path (публикует <see cref="RunAiReviewRequested"/>) + всю матрицу
///     отказов (403/409/404/400), в каждом из которых event НЕ публикуется.
/// </summary>
public sealed class StudentRerunEndpointTests : AssignmentReviewServiceTestsBase
{
    public StudentRerunEndpointTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        Factory.VcsProvider.Reset();
        Factory.AiClient.Reset();
    }

    [Fact]
    public async Task Owner_OnMinor_Queues_PublishesRunAiReviewRequested()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        Guid reviewId = await SeedReviewAsync(userId, AiReviewVerdict.MINOR_ISSUES);

        HttpResponseMessage response = await PostRerunAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RunAiReviewRequested published = OutboxCollector.OfType<RunAiReviewRequested>().Single();
        Assert.Equal(reviewId, published.AiReviewId);
        Assert.Null(published.ModelOverride);
        // Студент не может ни override'ить модель, ни снимать hard-cap на размер diff'а.
        Assert.False(published.AllowOversizedDiff);
        Assert.False(published.ForceFresh);
    }

    [Fact]
    public async Task Admin_OnMinor_Queues()
    {
        // owner-or-admin: админ тоже может (post-purchase support).
        Guid studentId = Guid.NewGuid();
        AuthenticateAs("platform-admin", Guid.NewGuid());

        Guid reviewId = await SeedReviewAsync(studentId, AiReviewVerdict.MINOR_ISSUES);

        HttpResponseMessage response = await PostRerunAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    [Fact]
    public async Task NotOwner_403_DoesNotPublish()
    {
        Guid ownerUserId = Guid.NewGuid();
        Guid otherUserId = Guid.NewGuid();
        AuthenticateAs("platform-participant", otherUserId);

        Guid reviewId = await SeedReviewAsync(ownerUserId, AiReviewVerdict.MINOR_ISSUES);

        HttpResponseMessage response = await PostRerunAsync(reviewId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    [Theory]
    [InlineData(AiReviewVerdict.LOOKS_GOOD)]
    [InlineData(AiReviewVerdict.MAJOR_ISSUES)]
    [InlineData(AiReviewVerdict.OFF_TOPIC)]
    public async Task AnyCompletedVerdict_Queues(AiReviewVerdict verdict)
    {
        // #976: гейт больше не смотрит на ПОСЛЕДНИЙ вердикт. Пост-approve re-run, вернувший
        // MAJOR_ISSUES/OFF_TOPIC, обязан оставлять студенту путь «поправил → перепроверил» —
        // ре-сабмит уже невозможен (submission APPROVED).
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        Guid reviewId = await SeedReviewAsync(userId, verdict);

        HttpResponseMessage response = await PostRerunAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        RunAiReviewRequested published = OutboxCollector.OfType<RunAiReviewRequested>().Single();
        Assert.Equal(reviewId, published.AiReviewId);
        Assert.Null(published.ModelOverride);
        Assert.False(published.AllowOversizedDiff);
    }

    [Fact]
    public async Task LastIterationFailed_AfterCompletedOne_Queues()
    {
        // #976-регрессия: FAILED-итерация обнуляет denorm LatestVerdict. Старый гейт на
        // MINOR_ISSUES после упавшей перепроверки запирал студента навсегда.
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        Guid reviewId = await SeedReviewAsync(
            userId,
            AiReviewVerdict.MINOR_ISSUES,
            status: AiReviewStatus.FAILED,
            withTrailingFailedIteration: true);

        HttpResponseMessage response = await PostRerunAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(reviewId, OutboxCollector.OfType<RunAiReviewRequested>().Single().AiReviewId);
    }

    [Fact]
    public async Task NoCompletedIteration_409_DoesNotPublish()
    {
        // Свежий QUEUED review без единой завершённой итерации: перепроверять нечего.
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        Guid reviewId = await SeedReviewAsync(
            userId,
            verdict: null,
            status: AiReviewStatus.QUEUED,
            withCompletedIteration: false);

        HttpResponseMessage response = await PostRerunAsync(reviewId);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("review.rerun.not_available", body, StringComparison.Ordinal);
        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    [Fact]
    public async Task OnlyFailedIterations_409_DoesNotPublish()
    {
        // Все попытки упали (провайдер лёг) — вердикта не было, доработка недоступна.
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        Guid reviewId = await SeedReviewAsync(
            userId,
            verdict: null,
            status: AiReviewStatus.FAILED,
            withCompletedIteration: false,
            withTrailingFailedIteration: true);

        HttpResponseMessage response = await PostRerunAsync(reviewId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    [Fact]
    public async Task AlreadyRunning_DoesNotPublish()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        Guid reviewId = await SeedReviewAsync(
            userId, AiReviewVerdict.MINOR_ISSUES, status: AiReviewStatus.RUNNING);

        HttpResponseMessage response = await PostRerunAsync(reviewId);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    [Fact]
    public async Task ReviewMissing_404_DoesNotPublish()
    {
        AuthenticateAs("platform-participant");
        Guid bogusId = Guid.NewGuid();

        HttpResponseMessage response = await PostRerunAsync(bogusId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    [Fact]
    public async Task ReviewDisabled_409_DoesNotPublish()
    {
        // #355 master-switch off → доработка отбивается review.disabled до гейта вердикта.
        Guid userId = Guid.NewGuid();
        AuthenticateAs("platform-participant", userId);

        Guid reviewId = await SeedReviewAsync(userId, AiReviewVerdict.MINOR_ISSUES);
        await SeedReviewDisabledAsync();

        HttpResponseMessage response = await PostRerunAsync(reviewId);

        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("review.disabled", body, StringComparison.Ordinal);
        Assert.Empty(OutboxCollector.OfType<RunAiReviewRequested>());
    }

    private async Task SeedReviewDisabledAsync()
    {
        await ExecuteInDbAsync(async db =>
        {
            AiModelSlot slot = AiModelSlot.Create("deepseek/deepseek-v4-pro", 0.2, 4000, 300).Value;
            AiModelSettings settings = AiModelSettings.Create(
                slot, Guid.NewGuid(), reviewerBasePrompt: null, reviewEnabled: false).Value;
            db.Set<AiModelSettings>().Add(settings);
            await db.SaveChangesAsync();
        });
    }

    private async Task<HttpResponseMessage> PostRerunAsync(Guid reviewId) =>
        await AppHttpClient.PostAsJsonAsync(
            $"/assignment-review/reviews/{reviewId}/student-rerun/", new { });

    /// <summary>
    ///     Сеет AiReview с одной COMPLETED-итерацией (вердикт <paramref name="verdict"/>) и,
    ///     опционально, упавшей итерацией поверх неё. Упавшая итерация обнуляет denorm
    ///     <c>LatestVerdict</c> — ровно как <c>AiReview.OnIterationFailed</c> в проде.
    /// </summary>
    private async Task<Guid> SeedReviewAsync(
        Guid userId,
        AiReviewVerdict? verdict,
        AiReviewStatus status = AiReviewStatus.READY,
        bool withCompletedIteration = true,
        bool withTrailingFailedIteration = false)
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

            if (withCompletedIteration)
            {
                AiReviewIteration completed = review.StartIteration("aaaa111");
                completed.Complete(
                    verdict ?? AiReviewVerdict.MINOR_ISSUES,
                    summary: "seed summary",
                    inlineCommentsCount: 0,
                    gitHubReviewId: null,
                    modelUsed: "test-model",
                    inputTokens: null,
                    outputTokens: null);
            }

            if (withTrailingFailedIteration)
            {
                AiReviewIteration failed = review.StartIteration("bbbb222");
                failed.Fail("review.llm.unavailable", "test-model");
            }

            db.Entry(review).Property(nameof(AiReview.Status)).CurrentValue = status;
            db.Entry(review).Property(nameof(AiReview.LatestVerdict)).CurrentValue =
                withTrailingFailedIteration ? null : verdict;
            db.AiReviews.Add(review);
            await db.SaveChangesAsync();
        });
        return reviewId;
    }
}
