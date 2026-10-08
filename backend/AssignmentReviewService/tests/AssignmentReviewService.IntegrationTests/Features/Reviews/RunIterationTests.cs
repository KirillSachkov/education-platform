using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Core.Features.Reviews.Handlers;
using AssignmentReviewService.Core.Vcs.Models;
using AssignmentReviewService.Domain.AiSettings;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.AI;
using Shared.Messaging.IntegrationEvents.AssignmentReview;
using SharedKernel;
using Wolverine;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

public sealed class RunIterationTests : AssignmentReviewServiceTestsBase
{
    private const string REPO = "test-org/student-pr";
    private const int PULL_NUMBER = 7;

    public RunIterationTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        // Reset fakes per test — base.InitializeAsync resets DB, мы дополнительно
        // сбрасываем in-memory fake state (он singleton на factory).
        Factory.VcsProvider.Reset();
        Factory.AiClient.Reset();
    }

    [Fact]
    public async Task HappyPath_LooksGood_PersistsCompletedIterationAndPostsReview()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        // Manual run-iteration is author/admin-only now (#16): students can't trigger
        // it (their first review is auto-run on submit). Pipeline tests act as admin.
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.AiClient.QueueResponse(
            "MINOR_ISSUES",
            "Solid implementation, fix naming.",
            ("src/Foo.cs", 5, "Rename `x` to `value`"));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected OK but got {response.StatusCode}. Body: {body}");

        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.Id == reviewId);
            Assert.NotNull(review);
            Assert.Equal(AiReviewStatus.READY, review!.Status);
            Assert.Equal(AiReviewVerdict.MINOR_ISSUES, review.LatestVerdict);
            Assert.Single(review.Iterations);

            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            Assert.Equal(AiReviewVerdict.MINOR_ISSUES, iteration.Verdict);
            Assert.Equal(1, iteration.IterationNumber);
            Assert.Equal(1, iteration.InlineCommentsCount);
            Assert.Equal(42L, iteration.GitHubReviewId);
            Assert.Equal("deadbeef", iteration.CommitSha);
        });

        // Suppress unused warning — body is captured for debugging in case of future failures.
        _ = body;

        Assert.Single(Factory.VcsProvider.PostedReviews);
        VcsReviewRequest posted = Factory.VcsProvider.PostedReviews.Single().Request;
        Assert.Single(posted.InlineComments);
        Assert.Equal("src/Foo.cs", posted.InlineComments[0].Path);
        // #383: posted review uses a human-readable headline, not the raw verdict label.
        // MINOR_ISSUES auto-approves, so the body says «Задание принято» and carries the AI
        // summary — never the bare "MINOR_ISSUES" token (which used to mislead students).
        Assert.Contains("Задание принято", posted.SummaryBody, StringComparison.Ordinal);
        Assert.Contains("Solid implementation, fix naming.", posted.SummaryBody, StringComparison.Ordinal);
        Assert.DoesNotContain("MINOR_ISSUES", posted.SummaryBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Installation_owner_matching_is_case_insensitive()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("Test-Org");
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Everything is fine.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
        });
    }

    [Fact]
    public async Task LooksGood_NoComments_DoesNotPostReview()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        // Manual run-iteration is author/admin-only now (#16): students can't trigger
        // it (their first review is auto-run on submit). Pipeline tests act as admin.
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // Default verdict LOOKS_GOOD, no inline comments → skip posting.
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Everything is fine.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Factory.VcsProvider.PostedReviews);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Null(iteration.GitHubReviewId);
            Assert.Equal(AiReviewVerdict.LOOKS_GOOD, iteration.Verdict);
        });
    }

    [Fact]
    public async Task RunIteration_IncludesStudentReplies_InPrompt_WhenPresent()
    {
        // #713 deliverable B: реплики студента к прошлым замечаниям, созданные ДО запуска,
        // попадают в prompt как контекст-блок #0d.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        await ExecuteInDbAsync(async db =>
        {
            StudentPrMessage msg = StudentPrMessage.Create(
                aiReviewId: reviewId,
                gitHubCommentId: 4242L,
                inReplyToGitHubId: 4200L,
                kind: StudentPrMessageKind.REVIEW_COMMENT,
                authorGithubLogin: "student",
                body: "Я оставил этот файл специально — так требует задание.",
                path: "src/Foo.cs",
                line: 5,
                commentUrl: "https://github.com/test-org/student-pr/pull/7#discussion_r4242",
                createdAtGithub: DateTimeOffset.UtcNow.AddMinutes(-10),
                ingestedAt: DateTimeOffset.UtcNow.AddMinutes(-10));
            db.StudentPrMessages.Add(msg);
            await db.SaveChangesAsync();
        });

        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Всё хорошо.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AiGenerationRequest lastRequest = Factory.AiClient.ReceivedRequests[^1];
        Assert.Contains("# 0d. Реплики студента", lastRequest.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("Я оставил этот файл специально", lastRequest.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("src/Foo.cs:5", lastRequest.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunIteration_NoStudentRepliesBlock_WhenNoMessages()
    {
        // #713: без реплик студента блок #0d в prompt не добавляется.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Всё хорошо.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AiGenerationRequest lastRequest = Factory.AiClient.ReceivedRequests[^1];
        Assert.DoesNotContain("# 0d. Реплики студента", lastRequest.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("UNTRUSTED_STUDENT_REPLIES", lastRequest.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunIteration_ManyStudentReplies_KeepsNewestWithinLimit_DropsOldest()
    {
        // #713 FIX-1: bounded read последних 30 реплик — при 33 в промпт попадают 30 свежих,
        // 3 самых старых дропаются (ORDER BY created_at_github DESC LIMIT 30).
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // 33 короткие реплики с различающимися отметками времени (все в прошлом → до запуска).
        DateTimeOffset baseTime = DateTimeOffset.UtcNow.AddHours(-1);
        for (int i = 0; i < 33; i++)
        {
            await SeedStudentMessageAsync(
                reviewId,
                body: $"REPLY-MARK-{i:D2}",
                createdAt: baseTime.AddSeconds(i),
                gitHubCommentId: 5000L + i);
        }

        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Всё хорошо.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string? prompt = Factory.AiClient.ReceivedRequests[^1].UserPrompt;
        // Newest 30 (i = 3..32) — присутствуют.
        Assert.Contains("REPLY-MARK-32", prompt, StringComparison.Ordinal);
        Assert.Contains("REPLY-MARK-03", prompt, StringComparison.Ordinal);
        // Oldest 3 (i = 0..2) — за пределами лимита, дропнуты.
        Assert.DoesNotContain("REPLY-MARK-00", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("REPLY-MARK-01", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("REPLY-MARK-02", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunIteration_StudentReplies_CharCap_KeepsNewest_DropsOldest()
    {
        // #713 FIX-1: при переполнении char-cap блока реплик оставляем САМЫЕ СВЕЖИЕ
        // (они относятся к текущему состоянию PR), старые дропаются.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // 8 крупных реплик (~810 симв.) — суммарно далеко за cap 4000, поэтому влезут
        // только несколько самых свежих.
        DateTimeOffset baseTime = DateTimeOffset.UtcNow.AddHours(-1);
        for (int i = 0; i < 8; i++)
        {
            await SeedStudentMessageAsync(
                reviewId,
                body: $"CAPMARK-{i:D2}-" + new string('x', 800),
                createdAt: baseTime.AddSeconds(i),
                gitHubCommentId: 6000L + i);
        }

        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Всё хорошо.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string? prompt = Factory.AiClient.ReceivedRequests[^1].UserPrompt;
        // Самая свежая реплика включена всегда.
        Assert.Contains("CAPMARK-07", prompt, StringComparison.Ordinal);
        // Самая старая вытеснена char-cap'ом.
        Assert.DoesNotContain("CAPMARK-00", prompt, StringComparison.Ordinal);
    }

    private async Task SeedStudentMessageAsync(
        Guid reviewId, string body, DateTimeOffset createdAt, long gitHubCommentId)
    {
        await ExecuteInDbAsync(async db =>
        {
            StudentPrMessage msg = StudentPrMessage.Create(
                aiReviewId: reviewId,
                gitHubCommentId: gitHubCommentId,
                inReplyToGitHubId: null,
                kind: StudentPrMessageKind.ISSUE_COMMENT,
                authorGithubLogin: "student",
                body: body,
                path: null,
                line: null,
                commentUrl: $"https://github.com/{REPO}/pull/{PULL_NUMBER}#c{gitHubCommentId}",
                createdAtGithub: createdAt,
                ingestedAt: createdAt);
            db.StudentPrMessages.Add(msg);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task SingleFileAboveBatchSize_BelowHardCap_ReviewedInOwnBatch()
    {
        // #18 chunking: a single oversized file — above MaxDiffAdditions=1500 (per-batch)
        // but below HardMaxDiffAdditions=20000 (reject cap, #546) — is NOT rejected. DiffChunker
        // puts it in its own batch and the LLM reviews it. Hard-cap reject (>20000) is
        // covered by HugeDiff_AboveHardCap_FailsGracefully.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("huge.cs", null, "modified", Additions: 5000, Deletions: 0,
                Patch: "@@ -1 +1,5000 @@\n+...", Hunks: []);
            VcsDiff diff = new("deadbeef", [file], TotalAdditions: 5000, TotalDeletions: 0);
            return Task.FromResult(Result.Success<VcsDiff, Error>(diff));
        };
        Factory.AiClient.QueueResponse("MINOR_ISSUES", "Большой файл, но проверяемо.", ("huge.cs", 10, "nit"));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Got {response.StatusCode}: {body}");

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            Assert.Equal(AiReviewVerdict.MINOR_ISSUES, iteration.Verdict);
        });

        // Single oversized file → exactly one batch → one LLM call.
        Assert.Single(Factory.AiClient.ReceivedRequests);
    }

    [Fact]
    public async Task NoInstallation_ReturnsError_NoIterationCreated()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        // Manual run-iteration is author/admin-only now (#16): students can't trigger
        // it (their first review is auto-run on submit). Pipeline tests act as admin.
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        // NO installation seeded.

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        // Endpoint accepts (200) — handler runs in worker scope and returns the
        // error before any iteration row is persisted (#357: pre-acquireLock failures
        // don't surface in DB but go to logs).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Empty(review.Iterations);
            Assert.Equal(AiReviewStatus.QUEUED, review.Status);
        });
    }

    [Fact]
    public async Task GitHubUnavailable_PR_PersistsFailedIteration()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        // Manual run-iteration is author/admin-only now (#16): students can't trigger
        // it (their first review is auto-run on submit). Pipeline tests act as admin.
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.VcsProvider.PullRequestHandler = (_, _, _, _) =>
            Task.FromResult(Result.Failure<VcsPullRequest, Error>(
                Error.Failure("vcs.unavailable", "github unavailable")));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        // Endpoint accepts (200) — PR fetch fails before TryAcquireRunningLock,
        // so no iteration row is written; review remains QUEUED (recoverable retry).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // PR fetch fails before we register iteration — review still QUEUED.
        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Empty(review.Iterations);
            Assert.Equal(AiReviewStatus.QUEUED, review.Status);
        });
    }

    [Fact]
    public async Task LlmUnavailable_PersistsFailedIteration()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        // Manual run-iteration is author/admin-only now (#16): students can't trigger
        // it (their first review is auto-run on submit). Pipeline tests act as admin.
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // Sticky — провайдер недоступен на ВСЕХ ретраях (#405), иначе 2-я попытка получила бы
        // default-успех из пустой очереди фейка.
        Factory.AiClient.QueueFailureSticky(Error.Failure("ai.upstream.unavailable", "polza 503"));

        // Endpoint accepts (200) — LLM call happens in worker scope; failure is
        // persisted as FAILED iteration row.
        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.FAILED, review.Status);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal("review.llm.unavailable", iteration.FailureReason);
        });

        // #405: провайдер дёргался MaxAttempts(=3) раз прежде чем сдаться.
        Assert.Equal(3, Factory.AiClient.ReceivedRequests.Count);
    }

    [Fact]
    public async Task LlmThrows_PersistsFailedIteration_NotStuckRunning()
    {
        // Regression (#334): if the LLM call THROWS instead of returning a failure
        // Result (e.g. OperationCanceledException on request-token timeout / Wolverine
        // redelivery), it must NOT escape — the running-lock is already held, so an
        // escaped exception leaves the review stuck in RUNNING forever. It must be
        // caught and persisted as a FAILED iteration (lock released, recoverable).
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // Sticky — кидает на ВСЕХ ретраях (#405).
        Factory.AiClient.QueueThrowSticky(new OperationCanceledException("simulated timeout"));

        // Endpoint accepts (200); handler catches the throw and persists FAILED
        // iteration (no stuck RUNNING).
        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.FAILED, review.Status); // NOT stuck RUNNING
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.FAILED, iteration.Status);
            Assert.Equal("review.llm.unavailable", iteration.FailureReason);
        });
    }

    [Fact]
    public async Task LlmTransientFailure_RetriesAndRecovers_PersistsCompleted()
    {
        // #405: один транзиентный сбой провайдера → ретрай → 2-я попытка успешна
        // (default LOOKS_GOOD из фейка) → review завершается READY, не FAILED.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // НЕ sticky — фейлит только 1-й вызов, дальше фейк отдаёт default LOOKS_GOOD.
        Factory.AiClient.QueueFailure(Error.Failure("ai.upstream.unavailable", "transient 503"));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            Assert.Equal(AiReviewVerdict.LOOKS_GOOD, iteration.Verdict);
        });

        // Ровно 2 вызова: фейл + успешный ретрай.
        Assert.Equal(2, Factory.AiClient.ReceivedRequests.Count);
    }

    [Fact]
    public async Task InvalidOutput_ThenValid_RecoversViaReformatRetry()
    {
        // #928: модель отдала битый/усечённый JSON (ai.output.invalid — reasoning-модель
        // упёрлась в output-бюджет посреди JSON). Это НЕ недоступность провайдера: reformat-
        // retry (temp 0, «VALID JSON ONLY») внутри ReviewSingleBatchAsync получает валидный
        // ответ → итерация завершается COMPLETED, а не FAILED «провайдер недоступен».
        // Регрессия: раньше ai.output.invalid из GenerateAsync сразу уходил в LlmUnavailable
        // мимо reformat-retry, и задание не проверялось.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // 1-й вызов — битый вывод; reformat-retry (2-й вызов) — валидный ответ.
        Factory.AiClient.QueueFailure(AiErrors.OutputInvalid());
        Factory.AiClient.QueueResponse(
            "MINOR_ISSUES",
            "Recovered after reformat retry.",
            ("src/Foo.cs", 5, "Rename `x` to `value`"));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            Assert.Equal(AiReviewVerdict.MINOR_ISSUES, review.LatestVerdict);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            Assert.Equal(AiReviewVerdict.MINOR_ISSUES, iteration.Verdict);
            Assert.Null(iteration.FailureReason);
        });

        // Ровно 2 вызова: битый + reformat-retry (без 3× долбёжки провайдера).
        Assert.Equal(2, Factory.AiClient.ReceivedRequests.Count);
    }

    [Fact]
    public async Task InvalidOutputSticky_FailsAsInvalidOutput_NotUnavailable()
    {
        // #928: модель СТАБИЛЬНО отдаёт битый вывод (ai.output.invalid и на reformat-retry).
        // Итерация фиксируется FAILED с failure_reason = review.llm.invalid_output (UI →
        // «AI вернул некорректный ответ, попробуй ещё раз»), а НЕ review.llm.unavailable
        // («провайдер недоступен»). И reformat-retry делается ровно один раз внутри
        // AiReviewer — RunReviewerWithRetryAsync НЕ гоняет провайдер 3× (invalid_output не
        // ретраибельна), поэтому вызовов ровно 2, а не 3.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.AiClient.QueueFailureSticky(AiErrors.OutputInvalid());

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.FAILED, review.Status);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.FAILED, iteration.Status);
            Assert.Equal("review.llm.invalid_output", iteration.FailureReason);
        });

        // Ровно 2 вызова (первичный + один reformat-retry), НЕ 3 — invalid_output не
        // считается транзиентной infra-ошибкой, RunReviewerWithRetryAsync её не ретраит.
        Assert.Equal(2, Factory.AiClient.ReceivedRequests.Count);
    }

    [Fact]
    public async Task EmptyThenInvalid_RecoversViaReformatRetry_ThreeCalls()
    {
        // #928 (edge-комбо): пустой ответ (#405) → empty-retry возвращает ai.output.invalid
        // → reformat-retry (#928) добирает валидный ответ. firstCallInvalidOutput считается
        // ПОСЛЕ empty-ретрая, поэтому битый вывод из ретрая корректно уходит в reformat, а не
        // в «провайдер недоступен». Всего 3 вызова в одном ReviewSingleBatchAsync; финальная
        // классификация (success) не ретраится RunReviewerWithRetryAsync.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.AiClient.QueueFailure(AiErrors.OutputEmpty());     // 1: пустой ответ
        Factory.AiClient.QueueFailure(AiErrors.OutputInvalid());   // 2: empty-retry → битый
        Factory.AiClient.QueueResponse(                            // 3: reformat-retry → валидный
            "LOOKS_GOOD",
            "Recovered after empty then invalid.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            Assert.Equal(AiReviewVerdict.LOOKS_GOOD, review.LatestVerdict);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            Assert.Null(iteration.FailureReason);
        });

        // Ровно 3 вызова: пустой + empty-retry(битый) + reformat-retry(успех).
        Assert.Equal(3, Factory.AiClient.ReceivedRequests.Count);
    }

    [Fact]
    public async Task LlmInvalidOutput_RetriesAndStillFails_PersistsFailedIteration()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        // Manual run-iteration is author/admin-only now (#16): students can't trigger
        // it (their first review is auto-run on submit). Pipeline tests act as admin.
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.AiClient.QueueRawJson("{}"); // missing required fields
        Factory.AiClient.QueueRawJson("{ \"verdict\": \"BANANAS\" }"); // invalid enum

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        // Endpoint accepts (200); two LLM attempts happen in worker scope.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, Factory.AiClient.ReceivedRequests.Count);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal("review.llm.invalid_output", iteration.FailureReason);
        });
    }

    [Fact]
    public async Task RepoNotInInstallation_MapsCorrectError()
    {
        // After #357 the endpoint is fire-and-forget — pre-acquireLock failures
        // (PR-fetch errors etc.) only surface in handler logs, not in the HTTP
        // response. Drive the worker handler directly via DI to assert the precise
        // error-code mapping (vcs.repo.not_in_installation → review.repo.not_in_installation).
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.VcsProvider.PullRequestHandler = (_, _, _, _) =>
            Task.FromResult(Result.Failure<VcsPullRequest, Error>(
                Error.Failure("vcs.repo.not_in_installation", "boom")));

        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        global::Core.Abstractions.ICommandHandler<
                AssignmentReviewService.Core.Features.Reviews.UseCases.RunIterationResponse,
                AssignmentReviewService.Core.Features.Reviews.UseCases.RunIterationCommand> handler =
            scope.ServiceProvider.GetRequiredService<global::Core.Abstractions.ICommandHandler<
                AssignmentReviewService.Core.Features.Reviews.UseCases.RunIterationResponse,
                AssignmentReviewService.Core.Features.Reviews.UseCases.RunIterationCommand>>();
        Result<AssignmentReviewService.Core.Features.Reviews.UseCases.RunIterationResponse, Error> result =
            await handler.Handle(
                new AssignmentReviewService.Core.Features.Reviews.UseCases.RunIterationCommand(
                    reviewId, ModelOverride: null, SystemTriggered: true),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("review.repo.not_in_installation", result.Error.Messages[0].Code);
    }

    [Fact]
    public async Task UnauthenticatedCaller_Returns401()
    {
        Guid reviewId = await SeedReviewAsync(Guid.NewGuid());
        // No AuthenticateAs — anonymous request.

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CallerNotOwner_Returns403_ish()
    {
        Guid ownerUserId = Guid.NewGuid();
        Guid otherUserId = Guid.NewGuid();
        AuthenticateAs("platform-participant", otherUserId);

        Guid reviewId = await SeedReviewAsync(ownerUserId);
        await SeedInstallationAsync("test-org");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("review.access_denied", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StudentOwner_CannotManuallyRunIteration_Returns403()
    {
        // #16: manual run-iteration is author/admin-only. The submitting student
        // (review.UserId) is explicitly NOT allowed — their first review is auto-run
        // on submit (SystemTriggered), and re-checks are author-initiated.
        Guid studentUserId = Guid.NewGuid();
        AuthenticateAs("platform-participant", studentUserId);

        Guid reviewId = await SeedReviewAsync(studentUserId);
        await SeedInstallationAsync("test-org");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains("review.access_denied", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GitHubReviewBody_EscapesMentions_PreventsNotifications()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        // Manual run-iteration is author/admin-only now (#16): students can't trigger
        // it (their first review is auto-run on submit). Pipeline tests act as admin.
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // AI возвращает summary И inline-comment с @mention — оба должны быть
        // заменены на fullwidth ＠ (иначе GitHub ping'нет реальных юзеров).
        Factory.AiClient.QueueResponse(
            "MINOR_ISSUES",
            "Hey @octocat, please review this PR. Also notify @admin",
            ("src/Foo.cs", 5, "Heads up @maintainer — typo here"));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Single(Factory.VcsProvider.PostedReviews);
        VcsReviewRequest posted = Factory.VcsProvider.PostedReviews.Single().Request;

        // Summary body — без raw @mention'ов.
        Assert.DoesNotContain("@octocat", posted.SummaryBody, StringComparison.Ordinal);
        Assert.DoesNotContain("@admin", posted.SummaryBody, StringComparison.Ordinal);
        Assert.Contains("＠octocat", posted.SummaryBody, StringComparison.Ordinal);

        // Inline-comment body — тоже escape'нут (prompt-injection защита).
        Assert.Single(posted.InlineComments);
        string inlineBody = posted.InlineComments[0].Body;
        Assert.DoesNotContain("@maintainer", inlineBody, StringComparison.Ordinal);
        Assert.Contains("＠maintainer", inlineBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Incremental_NewCommit_UsesCompareDiff_AndCarriesPriorContext()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        // Admin bypasses ownership — re-review инициирует автор/ревьюер, не студент.
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // First review on commit "sha-old". CommitSha итерации берётся из diff.HeadSha,
        // поэтому full-diff fake тоже должен репортить "sha-old".
        Factory.VcsProvider.PullRequestHandler = (_, repo, num, _) =>
            Task.FromResult(Result.Success<VcsPullRequest, Error>(
                MakePr(repo, num, headSha: "sha-old")));
        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("src/Foo.cs", null, "modified", 5, 0,
                Patch: "@@ -1 +1,5 @@\n+initial\n", Hunks: []);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("sha-old", [file], 5, 0)));
        };
        Factory.AiClient.QueueResponse("MAJOR_ISSUES", "Не хватает обработки ошибок.");
        HttpResponseMessage first = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second review — new head "sha-new". Должен идти через CompareAsync.
        Factory.AiClient.Reset();
        Factory.VcsProvider.PullRequestHandler = (_, repo, num, _) =>
            Task.FromResult(Result.Success<VcsPullRequest, Error>(
                MakePr(repo, num, headSha: "sha-new")));

        bool comparedCalled = false;
        string? capturedBaseSha = null;
        Factory.VcsProvider.CompareHandler = (_, _, baseSha, headSha, _) =>
        {
            comparedCalled = true;
            capturedBaseSha = baseSha;
            VcsDiffFile file = new("src/Foo.cs", null, "modified", 3, 0,
                Patch: "@@ -1 +1,3 @@\n+fixed\n", Hunks: []);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff(headSha, [file], 3, 0)));
        };
        // Diff handler must NOT be used on incremental path.
        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
            Task.FromResult(Result.Failure<VcsDiff, Error>(
                Error.Failure("should.not.be.called", "full diff used on incremental path")));
        Factory.AiClient.QueueResponse("MINOR_ISSUES", "Лучше, мелкие правки.");

        HttpResponseMessage second = await PostRunIterationAsync(reviewId);
        string body = await second.Content.ReadAsStringAsync();
        Assert.True(second.StatusCode == HttpStatusCode.OK, $"Got {second.StatusCode}: {body}");

        Assert.True(comparedCalled, "CompareAsync should be used for incremental re-review");
        Assert.Equal("sha-old", capturedBaseSha);

        // Prior verdict/summary должны попасть в prompt модели (TRUSTED Previous review).
        Shared.AI.AiGenerationRequest aiReq = Factory.AiClient.ReceivedRequests.Single();
        Assert.Contains("Previous review", aiReq.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("MAJOR_ISSUES", aiReq.UserPrompt, StringComparison.Ordinal);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(2, review.Iterations.Count);
            Assert.Equal(AiReviewVerdict.MINOR_ISSUES, review.LatestVerdict);
        });
    }

    [Fact]
    public async Task Incremental_CarriesPriorInlineCommentBodies_IntoReReviewPrompt()
    {
        // #383: на re-review модель должна видеть ТЕЛА своих прошлых inline-комментов
        // (что именно она просила исправить), чтобы не поднимать уже закрытые замечания.
        // Тела тянутся из GitHub по GitHubReviewId прошлой итерации (fake возвращает их
        // через ReviewCommentsHandler).
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // First review on "sha-old" — posts an inline comment → iteration gets a
        // GitHubReviewId (fake default = 42).
        Factory.VcsProvider.PullRequestHandler = (_, repo, num, _) =>
            Task.FromResult(Result.Success<VcsPullRequest, Error>(
                MakePr(repo, num, headSha: "sha-old")));
        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("src/Foo.cs", null, "modified", 5, 0,
                Patch: "@@ -1 +1,5 @@\n+initial\n", Hunks: []);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("sha-old", [file], 5, 0)));
        };
        Factory.AiClient.QueueResponse(
            "MAJOR_ISSUES", "Не хватает инициализации.",
            ("src/Foo.cs", 3, "Инициализируй Created_At в конструкторе."));
        HttpResponseMessage first = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second review — new head "sha-new". Prior review's inline comments are
        // fetched by GitHubReviewId and fed into the prompt.
        Factory.AiClient.Reset();
        Factory.VcsProvider.PullRequestHandler = (_, repo, num, _) =>
            Task.FromResult(Result.Success<VcsPullRequest, Error>(
                MakePr(repo, num, headSha: "sha-new")));
        Factory.VcsProvider.CompareHandler = (_, _, _, headSha, _) =>
        {
            VcsDiffFile file = new("src/Foo.cs", null, "modified", 3, 0,
                Patch: "@@ -1 +1,3 @@\n+fixed\n", Hunks: []);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff(headSha, [file], 3, 0)));
        };

        long? capturedReviewId = null;
        Factory.VcsProvider.ReviewCommentsHandler = (_, _, _, ghReviewId, _) =>
        {
            capturedReviewId = ghReviewId;
            return Task.FromResult(Result.Success<IReadOnlyList<VcsReviewComment>, Error>(
                [new VcsReviewComment("src/Foo.cs", 3, "Инициализируй Created_At в конструкторе.")]));
        };
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Теперь хорошо.");

        HttpResponseMessage second = await PostRunIterationAsync(reviewId);
        string body = await second.Content.ReadAsStringAsync();
        Assert.True(second.StatusCode == HttpStatusCode.OK, $"Got {second.StatusCode}: {body}");

        // Prior iteration's GitHubReviewId (fake default 42) was used to fetch comments.
        Assert.Equal(42L, capturedReviewId);

        // The prior inline-comment BODY must appear in the re-review prompt — this is
        // what lets the model verify it was fixed instead of re-raising it.
        Shared.AI.AiGenerationRequest aiReq = Factory.AiClient.ReceivedRequests.Single();
        Assert.Contains("Previous review", aiReq.UserPrompt, StringComparison.Ordinal);
        Assert.Contains(
            "Инициализируй Created_At в конструкторе.", aiReq.UserPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Incremental_NoNewChanges_ShortCircuits_NoLlmCall()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.VcsProvider.PullRequestHandler = (_, repo, num, _) =>
            Task.FromResult(Result.Success<VcsPullRequest, Error>(
                MakePr(repo, num, headSha: "sha-1")));
        Factory.AiClient.QueueResponse("MINOR_ISSUES", "Почини нейминг.");
        await PostRunIterationAsync(reviewId);

        // Re-run with a different head, but compare returns empty (no new commits).
        Factory.AiClient.Reset();
        Factory.VcsProvider.PullRequestHandler = (_, repo, num, _) =>
            Task.FromResult(Result.Success<VcsPullRequest, Error>(
                MakePr(repo, num, headSha: "sha-2")));
        Factory.VcsProvider.CompareHandler = (_, _, _, headSha, _) =>
            Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff(headSha, [], 0, 0)));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // No LLM call on empty incremental diff.
        Assert.Empty(Factory.AiClient.ReceivedRequests);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            // Still just one iteration — short-circuit replays the prior one.
            Assert.Single(review.Iterations);
        });
    }

    [Fact]
    public async Task Resubmit_AcrossSeparateReviews_SamePr_UsesFullDiff_AndCarriesPriorContext()
    {
        // Resubmit (#334) creates a NEW IssueSubmission → NEW AiReview for the SAME PR.
        // #581: a re-submission is a fresh evaluation of the WHOLE solution, so it MUST use
        // the FULL PR diff — NOT a cross-review incremental compare. Incremental compare on
        // resubmit only sees the newest commit, which made the reviewer falsely declare
        // already-implemented features "missing" (prod case naturalnayasmetanka/ds#18: 6
        // false MAJOR in a row). The prior review's verdict/summary IS still carried into the
        // prompt as trusted context so the model can verify-not-relitigate on the full diff.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        await SeedInstallationAsync("test-org");

        // --- First submission's review: completes on sha-old via full diff. ---
        Guid firstReviewId = await SeedReviewAsync(userId);
        Factory.VcsProvider.PullRequestHandler = (_, repo, num, _) =>
            Task.FromResult(Result.Success<VcsPullRequest, Error>(
                MakePr(repo, num, headSha: "sha-old")));
        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
            Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("sha-old",
                    [new VcsDiffFile("src/Foo.cs", null, "modified", 5, 0, "@@ -1 +1,5 @@\n+initial\n", [])],
                    5, 0)));
        Factory.AiClient.QueueResponse("MINOR_ISSUES", "Почини нейминг.");
        HttpResponseMessage first = await PostRunIterationAsync(firstReviewId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // --- Second submission's review (same repo+pull+user, new head): FULL diff. ---
        Guid secondReviewId = await SeedReviewAsync(userId);
        Factory.AiClient.Reset();
        Factory.VcsProvider.PullRequestHandler = (_, repo, num, _) =>
            Task.FromResult(Result.Success<VcsPullRequest, Error>(
                MakePr(repo, num, headSha: "sha-new")));

        bool fullDiffCalled = false;
        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            fullDiffCalled = true;
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("sha-new",
                    [new VcsDiffFile("src/Foo.cs", null, "modified", 8, 0, "@@ -1 +1,8 @@\n+whole solution\n", [])],
                    8, 0)));
        };
        // Cross-review incremental compare must NOT be used on resubmit (#581).
        Factory.VcsProvider.CompareHandler = (_, _, _, _, _) =>
            Task.FromResult(Result.Failure<VcsDiff, Error>(
                Error.Failure("should.not.be.called", "incremental compare used on cross-review resubmit")));
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Теперь хорошо.");

        HttpResponseMessage second = await PostRunIterationAsync(secondReviewId);
        string body = await second.Content.ReadAsStringAsync();
        Assert.True(second.StatusCode == HttpStatusCode.OK, $"Got {second.StatusCode}: {body}");

        Assert.True(fullDiffCalled, "Full PR diff should be used on resubmit — whole-solution re-evaluation (#581)");

        // Prior review's verdict carried into the prompt as trusted previous-review context.
        Shared.AI.AiGenerationRequest aiReq = Factory.AiClient.ReceivedRequests.Single();
        Assert.Contains("Previous review", aiReq.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("MINOR_ISSUES", aiReq.UserPrompt, StringComparison.Ordinal);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == secondReviewId);
            Assert.Single(review.Iterations);
            Assert.Equal(AiReviewVerdict.LOOKS_GOOD, review.LatestVerdict);
        });
    }

    [Fact]
    public async Task LargeDiff_ChunksAndAggregates_PersistsOneIteration()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // 4 source files × 800 additions = 3200 > MaxDiffAdditions(1500) → multi-batch.
        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile Mk(string path) => new(path, null, "modified", 800, 0,
                Patch: "@@ -1 +1,800 @@\n", Hunks: []);
            VcsDiff diff = new("deadbeef",
                [Mk("src/A.cs"), Mk("src/B.cs"), Mk("src/C.cs"), Mk("src/D.cs")],
                TotalAdditions: 3200, TotalDeletions: 0);
            return Task.FromResult(Result.Success<VcsDiff, Error>(diff));
        };

        // Per-batch responses: worst verdict should aggregate to MAJOR_ISSUES.
        Factory.AiClient.QueueResponse("MINOR_ISSUES", "Batch 1 ok.", ("src/A.cs", 5, "nit"));
        Factory.AiClient.QueueResponse("MAJOR_ISSUES", "Batch 2 broken.", ("src/C.cs", 9, "bug"));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Got {response.StatusCode}: {body}");

        // Multiple LLM calls (one per batch).
        Assert.True(Factory.AiClient.ReceivedRequests.Count >= 2);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            // Exactly ONE persisted iteration with the aggregated result.
            Assert.Single(review.Iterations);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewVerdict.MAJOR_ISSUES, iteration.Verdict);
            Assert.Equal(2, iteration.InlineCommentsCount);
        });

        // One posted review with merged comments.
        Assert.Single(Factory.VcsProvider.PostedReviews);
        Assert.Equal(2, Factory.VcsProvider.PostedReviews.Single().Request.InlineComments.Count);
    }

    [Fact]
    public async Task HugeDiff_AboveHardCap_FailsGracefully()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // 21000 reviewable additions > HardMaxDiffAdditions(20000, #546) → graceful fail.
        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("src/Huge.cs", null, "modified", 21000, 0,
                Patch: "+...", Hunks: []);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("deadbeef", [file], 21000, 0)));
        };

        // Endpoint accepts (200); diff hard-cap reject persists FAILED iteration
        // with the precise error code — assertion moved from response body to DB.
        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.FAILED, review.Status);
            Assert.Equal("review.diff.too_large", review.Iterations.Single().FailureReason);
        });

        // No LLM call on hard-cap reject.
        Assert.Empty(Factory.AiClient.ReceivedRequests);

        // #546: пропуск из-за большого diff'а сигналит автору курса (без AllowOversizedDiff).
        AiReviewOversizedSkipped skipped =
            Factory.OutboxCollector.OfType<AiReviewOversizedSkipped>().Single();
        Assert.Equal(reviewId, skipped.AiReviewId);
        Assert.Equal(REPO, skipped.RepoFullName);
        Assert.Equal(PULL_NUMBER, skipped.PullNumber);
        Assert.NotEqual(Guid.Empty, skipped.AuthorId);
    }

    [Fact]
    public async Task HugeDiff_SecondTooLargeFailure_DoesNotRepublishOversizedEvent()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("src/Huge.cs", null, "modified", 21000, 0,
                Patch: "+...", Hunks: []);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("deadbeef", [file], 21000, 0)));
        };

        // Два прогона подряд (FAILED iteration не блокирует retry) — событие публикуется
        // только на ПЕРВЫЙ too_large-фейл этого review (#546, анти-флуд автору).
        await PostRunIterationAsync(reviewId);
        await PostRunIterationAsync(reviewId);

        Assert.Single(Factory.OutboxCollector.OfType<AiReviewOversizedSkipped>());
    }

    [Fact]
    public async Task HugeDiff_ManualRunWithAllowOversized_ReviewsWholePr_NoSkipEvent()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // Один файл на 21000 строк (> hard-cap 20000) — файл больше batch-лимита идёт
        // отдельным batch'ем, так что ровно один LLM call.
        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("src/Huge.cs", null, "modified", 21000, 0,
                Patch: "@@ -1 +1,21000 @@\n", Hunks: []);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("deadbeef", [file], 21000, 0)));
        };
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Большой, но решает задачу.");

        // Ручной «Перепроверить» (#405): AllowOversizedDiff=true снимает hard-cap.
        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.InvokeAsync(new RunAiReviewRequested(reviewId, null, AllowOversizedDiff: true));
        }

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            Assert.Equal(AiReviewVerdict.LOOKS_GOOD, review.Iterations.Single().Verdict);
        });

        // Cap снят вручную — событие «авто-проверка пропущена» публиковаться не должно.
        Assert.Empty(Factory.OutboxCollector.OfType<AiReviewOversizedSkipped>());
    }

    [Fact]
    public async Task HugeDiff_AboveManualEmergencyCap_DoesNotCallLlm()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("src/Generated.cs", null, "modified", 100_001, 0,
                Patch: "+...", Hunks: []);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("deadbeef", [file], 100_001, 0)));
        };

        await using (AsyncServiceScope scope = Services.CreateAsyncScope())
        {
            IMessageBus bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await bus.InvokeAsync(new RunAiReviewRequested(reviewId, null, AllowOversizedDiff: true));
        }

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.FAILED, review.Status);
            Assert.Equal("review.diff.too_large", review.Iterations.Single().FailureReason);
        });
        Assert.Empty(Factory.AiClient.ReceivedRequests);
    }

    [Fact]
    public async Task OutOfDiffInlineComment_IsDropped_ReviewPostsOnlyValidComments()
    {
        // #368: GitHub rejects the WHOLE review with 422 if any single inline comment
        // points at a line not in the diff. We pre-filter such comments out so the
        // valid ones still land. AI here returns one in-diff comment (line 5) and one
        // out-of-diff comment (line 999); only line 5 must be posted.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        // Diff with parsed hunks covering new-file lines 1..10 only.
        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("src/Foo.cs", null, "modified", 10, 0,
                Patch: "@@ -1,10 +1,10 @@",
                Hunks: [new VcsHunk(OldStart: 1, OldLines: 10, NewStart: 1, NewLines: 10, Body: "@@ -1,10 +1,10 @@")]);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("deadbeef", [file], 10, 0)));
        };

        Factory.AiClient.QueueResponse(
            "MINOR_ISSUES",
            "Mostly fine, one nit.",
            ("src/Foo.cs", 5, "valid — line is in the diff"),
            ("src/Foo.cs", 999, "INVALID — line is outside the diff"));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Got {response.StatusCode}: {body}");

        // Posted review keeps only the in-diff comment.
        Assert.Single(Factory.VcsProvider.PostedReviews);
        VcsReviewRequest posted = Factory.VcsProvider.PostedReviews.Single().Request;
        Assert.Single(posted.InlineComments);
        Assert.Equal(5, posted.InlineComments[0].Line);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            // Count reflects what was actually posted, not the raw LLM output.
            Assert.Equal(1, iteration.InlineCommentsCount);
        });
    }

    [Fact]
    public async Task GitHub422WithComments_RetriesSummaryOnly_IterationCompleted()
    {
        // #368: even after pre-filtering, GitHub may still reject the payload with 422
        // (vcs.invalid_request) — e.g. an outdated commit_id or a position it won't take.
        // Instead of failing the whole iteration, retry ONCE summary-only so the review
        // still lands. Fake rejects any post that carries inline comments, accepts the
        // summary-only retry.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("src/Foo.cs", null, "modified", 10, 0,
                Patch: "@@ -1,10 +1,10 @@",
                Hunks: [new VcsHunk(1, 10, 1, 10, "@@ -1,10 +1,10 @@")]);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("deadbeef", [file], 10, 0)));
        };
        // Comment is in-diff (passes the pre-filter) — isolates the 422 fallback path.
        Factory.AiClient.QueueResponse(
            "MINOR_ISSUES", "Fix naming.", ("src/Foo.cs", 5, "rename x"));

        Factory.VcsProvider.PostReviewHandler = (_, repo, num, req, _) =>
            req.InlineComments.Count > 0
                ? Task.FromResult(Result.Failure<VcsPostedReview, Error>(
                    Error.Failure("vcs.invalid_request", "HTTP 422: line must be part of the diff")))
                : Task.FromResult(Result.Success<VcsPostedReview, Error>(
                    new VcsPostedReview(99, $"https://github.com/{repo}/pull/{num}#review-99")));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Got {response.StatusCode}: {body}");

        // Two posts: first with the comment (rejected), then summary-only (accepted).
        Assert.Equal(2, Factory.VcsProvider.PostedReviews.Count);
        Assert.Single(Factory.VcsProvider.PostedReviews[0].Request.InlineComments);
        Assert.Empty(Factory.VcsProvider.PostedReviews[1].Request.InlineComments);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            Assert.Equal(99L, iteration.GitHubReviewId);
            // Inline comments were dropped on the summary-only retry.
            Assert.Equal(0, iteration.InlineCommentsCount);
        });
    }

    [Fact]
    public async Task GitHub5xx_OnPost_NotRetried_PersistsFailedIteration()
    {
        // #368 guard: a 5xx / network failure (vcs.unavailable) is a real outage, NOT a
        // bad payload — it must NOT trigger the summary-only retry. The iteration fails
        // with review.github.unavailable and exactly one post attempt is made.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");

        Factory.VcsProvider.DiffHandler = (_, _, _, _) =>
        {
            VcsDiffFile file = new("src/Foo.cs", null, "modified", 10, 0,
                Patch: "@@ -1,10 +1,10 @@",
                Hunks: [new VcsHunk(1, 10, 1, 10, "@@ -1,10 +1,10 @@")]);
            return Task.FromResult(Result.Success<VcsDiff, Error>(
                new VcsDiff("deadbeef", [file], 10, 0)));
        };
        Factory.AiClient.QueueResponse(
            "MINOR_ISSUES", "Fix naming.", ("src/Foo.cs", 5, "rename x"));

        Factory.VcsProvider.PostReviewHandler = (_, _, _, _, _) =>
            Task.FromResult(Result.Failure<VcsPostedReview, Error>(
                Error.Failure("vcs.unavailable", "HTTP 503")));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Exactly one post attempt — no retry on a real outage.
        Assert.Single(Factory.VcsProvider.PostedReviews);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations)
                .FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.FAILED, review.Status);
            Assert.Equal("review.github.unavailable", review.Iterations.Single().FailureReason);
        });
    }

    private static VcsPullRequest MakePr(string repoFullName, int pullNumber, string headSha) => new(
        repoFullName,
        pullNumber,
        Title: $"Test PR #{pullNumber}",
        AuthorLogin: "test-student",
        HeadSha: headSha,
        HeadRef: "feature/test",
        BaseRef: "main",
        HtmlUrl: $"https://github.com/{repoFullName}/pull/{pullNumber}",
        State: "open",
        IsDraft: false);

    [Fact]
    public async Task ReviewDisabled_RejectsRun_NoIteration()
    {
        // #355: master switch off → ручной re-run отбивается review.disabled, итерация не пишется.
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        Guid reviewId = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        await SeedReviewDisabledAsync();

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);

        string body = await response.Content.ReadAsStringAsync();
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("review.disabled", body, StringComparison.Ordinal);

        await ExecuteInDbAsync(async db =>
        {
            AiReview? review = await db.AiReviews.Include(r => r.Iterations)
                .FirstOrDefaultAsync(r => r.Id == reviewId);
            Assert.NotNull(review);
            Assert.Empty(review!.Iterations);
        });
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
                repoFullName: REPO,
                pullNumber: PULL_NUMBER,
                pullRequestUrl: $"https://github.com/{REPO}/pull/{PULL_NUMBER}");
            // Force assigned id чтобы тест мог его использовать.
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
