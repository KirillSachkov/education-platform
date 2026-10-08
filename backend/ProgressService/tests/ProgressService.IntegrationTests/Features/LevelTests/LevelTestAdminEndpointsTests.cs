using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Quizzes;
using ProgressService.Contracts.Requests;
using ProgressService.Core.Features.LevelTests.Admin;
using ProgressService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace ProgressService.IntegrationTests.Features.LevelTests;

/// <summary>
///     Админ-аналитика level-test (#537): доступ только с Users.VIEW
///     (admin/moderator), агрегаты считаются по реальным попыткам,
///     список отдаёт новые первыми с снапшот-метриками.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class LevelTestAdminEndpointsTests : ProgressServiceTestsBase
{
    private const string SUBMIT_URL = "/progress/level-test/attempts";
    private const string OVERVIEW_URL = "/progress/level-test/admin/overview";
    private const string ATTEMPTS_URL = "/progress/level-test/admin/attempts";

    private static readonly Guid _question = Guid.NewGuid();
    private static readonly Guid _correctOption = Guid.NewGuid();

    public LevelTestAdminEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Overview_ForParticipant_ReturnsForbidden()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(OVERVIEW_URL);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Overview_AfterSubmits_AggregatesCountsLevelsAndSections()
    {
        Guid quizId = SeedSingleQuestionLevelTest();
        RemoveAuthentication();

        // Две анонимные попытки: 100% (SENIOR) и пустая (JUNIOR — нижний порог).
        await SubmitAsync(quizId, [new(_question, [_correctOption], null)]);
        await SubmitAsync(quizId, []);

        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        Envelope<LevelTestAdminOverviewResponse>? envelope = await AppHttpClient
            .GetFromJsonAsync<Envelope<LevelTestAdminOverviewResponse>>(OVERVIEW_URL);

        LevelTestAdminOverviewResponse overview = envelope!.Result!;
        Assert.Equal(2, overview.TotalAttempts);
        Assert.Equal(2, overview.AnonymousAttempts);
        Assert.Equal(0, overview.UserAttempts);
        Assert.Equal(2, overview.AttemptsLast7Days);
        Assert.Equal(50, overview.AveragePercent);
        Assert.Equal(2, overview.LevelDistribution.Sum(l => l.Count));
        Assert.Contains(overview.LevelDistribution, l => l.Level == "SENIOR" && l.Count == 1);
        LevelTestSectionAverageRow section = Assert.Single(overview.SectionAverages);
        Assert.Equal("basics", section.Key);
        Assert.Equal(2, section.Attempts);
        Assert.Equal(50, section.AveragePercent);
        Assert.True(overview.AttemptsByDay.Sum(d => d.Count) == 2);
    }

    [Fact]
    public async Task Attempts_ListsNewestFirst_WithSnapshotMetrics()
    {
        Guid quizId = SeedSingleQuestionLevelTest();
        RemoveAuthentication();
        await SubmitAsync(quizId, []);
        await SubmitAsync(quizId, [new(_question, [_correctOption], null)]);

        AuthenticateAs(Guid.NewGuid(), "platform-admin");
        Envelope<LevelTestAdminAttemptsResponse>? envelope = await AppHttpClient
            .GetFromJsonAsync<Envelope<LevelTestAdminAttemptsResponse>>($"{ATTEMPTS_URL}?limit=1");

        LevelTestAdminAttemptsResponse page = envelope!.Result!;
        Assert.Equal(2, page.TotalCount);
        LevelTestAdminAttemptRow newest = Assert.Single(page.Items);
        Assert.Null(newest.UserId);
        Assert.Equal(100, newest.OverallPercent);
        Assert.Equal(1, newest.AnsweredCount);
        Assert.Equal(1, newest.TotalQuestions);
        Assert.False(newest.IsClaimed);
    }

    private async Task SubmitAsync(Guid quizId, List<SubmitLevelTestAnswerItem> answers)
    {
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            SUBMIT_URL,
            new SubmitLevelTestAttemptRequest(quizId, Guid.NewGuid().ToString(), answers));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Guid SeedSingleQuestionLevelTest()
    {
        Guid quizId = Guid.NewGuid();
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            "LEVEL_TEST",
            PassingScorePercent: 0,
            [
                new QuizAnswerKeyQuestionDto(
                    _question, "SINGLE_CHOICE", "Что такое CLR?", "basics", "JUNIOR", [_correctOption], null),
            ],
            new LevelTestConfigDto(
                [new LevelThresholdDto("JUNIOR", 0), new LevelThresholdDto("SENIOR", 90)],
                [new LevelTestSectionDto("basics", "Основы", 1.0m, null)],
                FallbackCourseId: null)));

        return quizId;
    }
}
