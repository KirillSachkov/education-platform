using System.Net;
using System.Net.Http.Json;
using AssignmentReviewService.Contracts.AiSettings;
using AssignmentReviewService.Contracts.Reviews;
using AssignmentReviewService.Domain.AiSettings;
using AssignmentReviewService.Domain.Reviews;
using AssignmentReviewService.Domain.Vcs;
using AssignmentReviewService.IntegrationTests.Infrastructure;
using CSharpFunctionalExtensions;
using Microsoft.EntityFrameworkCore;
using Shared.AI;
using SharedKernel;

namespace AssignmentReviewService.IntegrationTests.Features.Reviews;

/// <summary>
///     #798 — цикл дозапроса файлов репозитория (need_files): happy path, потолки,
///     капы файлов, деградация без дерева, «флаг выключен = старое поведение»,
///     admin-тумблер. Матрица из AC issue #798.
/// </summary>
public sealed class RepoContextLoopTests : AssignmentReviewServiceTestsBase
{
    private const string REPO = "test-org/student-pr";
    private const int PULL_NUMBER = 7;

    public RepoContextLoopTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        Factory.VcsProvider.Reset();
        Factory.AiClient.Reset();
    }

    [Fact]
    public async Task ModelRequestsFile_SecondCallGetsContent_TelemetryPersisted()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        (Guid reviewId, Guid submissionId) = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        await EnableRepoContextAsync();

        Factory.VcsProvider.RepoFiles["src/LocationName.cs"] =
            "public sealed class LocationName { public static LocationName Create(string v) => new(); }";
        Factory.VcsProvider.RepoFiles["Api/Api.csproj"] =
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><ItemGroup><PackageReference Include=\"Dapper\" /></ItemGroup></Project>";

        Factory.AiClient.QueueResponseWithNeedFiles(
            "MAJOR_ISSUES", "Не вижу определение LocationName.", ["src/LocationName.cs"]);
        Factory.AiClient.QueueResponse("MINOR_ISSUES", "Всё на месте, мелкие замечания.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Два LLM-вызова: запрос файла → финальный вердикт.
        Assert.Equal(2, Factory.AiClient.ReceivedRequests.Count);

        AiGenerationRequest first = Factory.AiClient.ReceivedRequests[0];
        Assert.Contains("# 0e. Repository context", first.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("<UNTRUSTED_REPO_TREE>", first.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("src/LocationName.cs", first.UserPrompt, StringComparison.Ordinal);
        // Манифест зависимостей приходит с первого вызова.
        Assert.Contains("PackageReference", first.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("need_files", first.JsonSchema!.Schema, StringComparison.Ordinal);

        AiGenerationRequest second = Factory.AiClient.ReceivedRequests[1];
        Assert.Contains("# 0f. Requested repository files", second.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("public static LocationName Create", second.UserPrompt, StringComparison.Ordinal);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations).FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewStatus.READY, review.Status);
            Assert.Equal(AiReviewVerdict.MINOR_ISSUES, review.LatestVerdict);

            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            Assert.Equal(1, iteration.ContextRounds);
            Assert.NotNull(iteration.RequestedFiles);
            Assert.Equal(["src/LocationName.cs"], iteration.RequestedFiles!);
        });

        // Телеметрия дозапроса доезжает до by-submission DTO (панель проверки).
        HttpResponseMessage detail = await AppHttpClient.GetAsync(
            $"/assignment-review/reviews/by-submission/{submissionId}/");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Envelope<AiReviewDetailDto>? envelope = await detail.Content.ReadFromJsonAsync<Envelope<AiReviewDetailDto>>();
        AiReviewIterationDto iterationDto = Assert.Single(envelope!.Result!.Iterations);
        Assert.Equal(1, iterationDto.ContextRounds);
        Assert.Equal(["src/LocationName.cs"], iterationDto.RequestedFiles!);
    }

    [Fact]
    public async Task GreedyModel_IsCappedAtMaxExtraRounds_AndStillCompletes()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        (Guid reviewId, _) = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        await EnableRepoContextAsync();

        Factory.VcsProvider.RepoFiles["src/A.cs"] = "class A { }";
        Factory.VcsProvider.RepoFiles["src/B.cs"] = "class B { }";
        Factory.VcsProvider.RepoFiles["src/C.cs"] = "class C { }";

        // Модель просит файлы в КАЖДОМ ответе — потолок default MaxExtraRounds=2
        // должен остановить цикл на 1 + 2 вызовах и взять вердикт третьего ответа.
        Factory.AiClient.QueueResponseWithNeedFiles("MAJOR_ISSUES", "Нужен A.", ["src/A.cs"]);
        Factory.AiClient.QueueResponseWithNeedFiles("MAJOR_ISSUES", "Нужен B.", ["src/B.cs"]);
        Factory.AiClient.QueueResponseWithNeedFiles("LOOKS_GOOD", "Теперь всё видно.", ["src/C.cs"]);

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(3, Factory.AiClient.ReceivedRequests.Count);
        // Финальный вызов идёт с инструкцией «бюджет исчерпан, вердикт обязателен».
        Assert.Contains(
            "file-request budget is exhausted",
            Factory.AiClient.ReceivedRequests[2].UserPrompt,
            StringComparison.Ordinal);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations).FirstAsync(r => r.Id == reviewId);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            // Вердикт третьего ответа; его need_files проигнорирован.
            Assert.Equal(AiReviewVerdict.LOOKS_GOOD, iteration.Verdict);
            Assert.Equal(2, iteration.ContextRounds);
            Assert.Equal(["src/A.cs", "src/B.cs"], iteration.RequestedFiles!);
        });
    }

    [Fact]
    public async Task NeedFiles_AbovePerRoundCap_IsTrimmed_AndDuplicatesDropped()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        (Guid reviewId, _) = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        await EnableRepoContextAsync();

        for (int i = 1; i <= 8; i++)
            Factory.VcsProvider.RepoFiles[$"src/F{i}.cs"] = $"class F{i} {{ }}";

        // 8 путей + дубль → per-round cap (default 5) режет до первых пяти уникальных.
        Factory.AiClient.QueueResponseWithNeedFiles(
            "MAJOR_ISSUES",
            "Нужно много файлов.",
            ["src/F1.cs", "src/F1.cs", "src/F2.cs", "src/F3.cs", "src/F4.cs", "src/F5.cs", "src/F6.cs", "src/F7.cs"]);
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Достаточно.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(2, Factory.AiClient.ReceivedRequests.Count);
        string secondPrompt = Factory.AiClient.ReceivedRequests[1].UserPrompt!;
        Assert.Contains("class F5", secondPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("class F6", secondPrompt, StringComparison.Ordinal);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations).FirstAsync(r => r.Id == reviewId);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(
                ["src/F1.cs", "src/F2.cs", "src/F3.cs", "src/F4.cs", "src/F5.cs"],
                iteration.RequestedFiles!);
        });
    }

    [Fact]
    public async Task MissingAndOversizedFiles_ArePassedAsNotes_NotContent()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        (Guid reviewId, _) = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        await EnableRepoContextAsync();

        Factory.VcsProvider.RepoFiles["src/Good.cs"] = "class Good { }";
        // 60KB > MaxFileBytes (51200) — отсекается по размеру из дерева, без фетча.
        Factory.VcsProvider.RepoFiles["src/Huge.cs"] = new string('x', 60_000);

        Factory.AiClient.QueueResponseWithNeedFiles(
            "MAJOR_ISSUES", "Нужны файлы.", ["src/Missing.cs", "src/Huge.cs", "src/Good.cs"]);
        Factory.AiClient.QueueResponse("MINOR_ISSUES", "Ок.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string secondPrompt = Factory.AiClient.ReceivedRequests[1].UserPrompt!;
        Assert.Contains("not found in the repository tree", secondPrompt, StringComparison.Ordinal);
        Assert.Contains("too large", secondPrompt, StringComparison.Ordinal);
        Assert.Contains("class Good", secondPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 1000), secondPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FlagOff_SingleCall_OldPrompt_OldSchema_NoTelemetry()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        (Guid reviewId, _) = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        // Флаг НЕ включён (config default false, DB-row нет).

        Factory.VcsProvider.RepoFiles["src/LocationName.cs"] = "class LocationName { }";
        Factory.AiClient.QueueResponse("LOOKS_GOOD", "Всё хорошо.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AiGenerationRequest only = Assert.Single(Factory.AiClient.ReceivedRequests);
        Assert.DoesNotContain("# 0e. Repository context", only.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("need_files", only.JsonSchema!.Schema, StringComparison.Ordinal);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations).FirstAsync(r => r.Id == reviewId);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            Assert.Equal(0, iteration.ContextRounds);
            Assert.Null(iteration.RequestedFiles);
        });
    }

    [Fact]
    public async Task TreeFetchFailure_DegradesGracefully_ReviewCompletes()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        (Guid reviewId, _) = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        await EnableRepoContextAsync();

        Factory.VcsProvider.RepoTreeHandler = (_, _, _, _) =>
            Task.FromResult(Result.Failure<IReadOnlyList<AssignmentReviewService.Core.Vcs.Models.VcsRepoTreeEntry>, Error>(
                Error.Failure("vcs.unavailable", "GitHub недоступен")));

        Factory.AiClient.QueueResponse("MINOR_ISSUES", "Ревью без карты репо.");

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        AiGenerationRequest only = Assert.Single(Factory.AiClient.ReceivedRequests);
        // Инструкция дозапроса есть (need_files работает через contents API),
        // а блока дерева нет — сбой GitHub деградирует, не роняет итерацию.
        Assert.Contains("# 0e. Repository context", only.UserPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("<UNTRUSTED_REPO_TREE>", only.UserPrompt, StringComparison.Ordinal);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations).FirstAsync(r => r.Id == reviewId);
            Assert.Equal(AiReviewIterationStatus.COMPLETED, review.Iterations.Single().Status);
        });
    }

    [Fact]
    public async Task LlmFailure_MidLoop_RetryRestartsLoopWithFreshState()
    {
        Guid userId = AssignmentReviewServiceTestsBase.DefaultUserId;
        AuthenticateAs("platform-admin", userId);

        (Guid reviewId, _) = await SeedReviewAsync(userId);
        await SeedInstallationAsync("test-org");
        await EnableRepoContextAsync();

        Factory.VcsProvider.RepoFiles["src/A.cs"] = "class A { }";

        // Раунд 1: модель просит файл; раунд 2: провайдер падает (транзиентно).
        // RunReviewerWithRetryAsync (#405) ретраит ВЕСЬ прогон — цикл начинается заново
        // с чистого состояния; пустая очередь FakeAiClient отдаёт дефолтный LOOKS_GOOD.
        Factory.AiClient.QueueResponseWithNeedFiles("MAJOR_ISSUES", "Нужен A.", ["src/A.cs"]);
        Factory.AiClient.QueueFailure(Error.Failure("ai.unavailable", "провайдер недоступен"));

        HttpResponseMessage response = await PostRunIterationAsync(reviewId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // 3 вызова: need_files → failure → retry с дефолтным вердиктом.
        Assert.Equal(3, Factory.AiClient.ReceivedRequests.Count);

        await ExecuteInDbAsync(async db =>
        {
            AiReview review = await db.AiReviews.Include(r => r.Iterations).FirstAsync(r => r.Id == reviewId);
            AiReviewIteration iteration = review.Iterations.Single();
            Assert.Equal(AiReviewIterationStatus.COMPLETED, iteration.Status);
            Assert.Equal(AiReviewVerdict.LOOKS_GOOD, iteration.Verdict);
            // Телеметрия — от успешной попытки (свежий цикл без дозапросов),
            // артефакты упавшей попытки не протекают.
            Assert.Equal(0, iteration.ContextRounds);
            Assert.Null(iteration.RequestedFiles);
        });
    }

    [Fact]
    public async Task AdminSettings_RepoContextEnabled_RoundTrips()
    {
        AuthenticateAs("platform-admin");

        HttpResponseMessage getDefault = await AppHttpClient.GetAsync("/assignment-review/admin/ai-settings/");
        Assert.Equal(HttpStatusCode.OK, getDefault.StatusCode);
        Envelope<AssignmentReviewAiModelSettingsDto>? beforeEnv =
            await getDefault.Content.ReadFromJsonAsync<Envelope<AssignmentReviewAiModelSettingsDto>>();
        Assert.False(beforeEnv!.Result!.RepoContextEnabled);

        UpdateAiModelSettingsRequest enable = new(
            new AiModelSlotInputDto("deepseek/deepseek-v4-pro", 0.2, 16000, 300),
            ReviewerBasePrompt: null,
            ReviewEnabled: true,
            RepoContextEnabled: true);
        HttpResponseMessage put = await AppHttpClient.PutAsJsonAsync("/assignment-review/admin/ai-settings/", enable);
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        HttpResponseMessage getAfter = await AppHttpClient.GetAsync("/assignment-review/admin/ai-settings/");
        Envelope<AssignmentReviewAiModelSettingsDto>? afterEnv =
            await getAfter.Content.ReadFromJsonAsync<Envelope<AssignmentReviewAiModelSettingsDto>>();
        Assert.True(afterEnv!.Result!.RepoContextEnabled);
        Assert.True(afterEnv.Result.ReviewEnabled);
    }

    private async Task EnableRepoContextAsync()
    {
        await ExecuteInDbAsync(async db =>
        {
            AiModelSlot slot = AiModelSlot.Create("deepseek/deepseek-v4-pro", 0.2, 16000, 300).Value;
            // DB-row перекрывает config целиком → reviewEnabled тоже явно true,
            // иначе появление row выключило бы AI-ревью в тестах.
            AiModelSettings settings = AiModelSettings.Create(
                slot, Guid.NewGuid(), reviewerBasePrompt: null,
                reviewEnabled: true, repoContextEnabled: true).Value;
            db.Set<AiModelSettings>().Add(settings);
            await db.SaveChangesAsync();
        });
    }

    private async Task<(Guid ReviewId, Guid SubmissionId)> SeedReviewAsync(Guid userId)
    {
        Guid reviewId = Guid.NewGuid();
        Guid submissionId = Guid.NewGuid();
        await ExecuteInDbAsync(async db =>
        {
            AiReview review = AiReview.Create(
                submissionId: submissionId,
                issueId: Guid.NewGuid(),
                userId: userId,
                authorId: Guid.NewGuid(),
                provider: VcsProvider.GITHUB,
                repoFullName: REPO,
                pullNumber: PULL_NUMBER,
                pullRequestUrl: $"https://github.com/{REPO}/pull/{PULL_NUMBER}");
            db.Entry(review).Property("Id").CurrentValue = reviewId;
            db.AiReviews.Add(review);
            await db.SaveChangesAsync();
        });
        return (reviewId, submissionId);
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
