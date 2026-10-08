using System.Net;
using EducationContentService.Core.Features.Quizzes.Queries;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>
///     Публичный анонимный GET активного теста уровня (#478):
///     <c>GET /quizzes/level-test/active</c> — самый свежий PUBLISHED LEVEL_TEST,
///     студенческая проекция без ответов и скоринговых внутренностей конфига.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class GetActiveLevelTestTests : EducationContentServiceTestsBase
{
    private static readonly Guid _optionA = Guid.NewGuid();
    private static readonly Guid _optionB = Guid.NewGuid();

    public GetActiveLevelTestTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetActiveLevelTest_Anonymous_Returns200WithSectionsAndNeverLeaksAnswers()
    {
        CancellationToken ct = CancellationToken.None;
        Guid quizId = await SeedQuizAsync(QuizPurpose.LEVEL_TEST, publish: true, withConfig: true);

        // Аноним + DenyAll: Tier-1 публичное промо — entitlement checker не зовётся вовсе.
        EntitlementChecker.DenyAll();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/level-test/active", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // String-level контракт: ответы и скоринговые внутренности конфига не должны
        // присутствовать в JSON ни под каким ключом и ни в каком значении.
        string rawJson = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("correctOptionIds", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("referenceAnswer", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Эталонное объяснение", rawJson, StringComparison.Ordinal);
        Assert.DoesNotContain("weight", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recommendedCourseId", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("levelThresholds", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fallbackCourseId", rawJson, StringComparison.OrdinalIgnoreCase);

        LevelTestStudentDto dto = await ReadResultAsync<LevelTestStudentDto>(response);
        Assert.Equal(quizId, dto.Id);
        Assert.Equal(2, dto.Questions.Count);
        Assert.Equal(2, dto.TotalQuestions);

        // Вопросы несут section/difficulty — прогресс по секциям на фронте.
        Assert.Equal("csharp-basics", dto.Questions[0].Section);
        Assert.Equal("JUNIOR", dto.Questions[0].Difficulty);
        Assert.Equal(2, dto.Questions[0].Options.Count);
        Assert.Equal("csharp-advanced", dto.Questions[1].Section);
        Assert.Equal("SENIOR", dto.Questions[1].Difficulty);
        Assert.Empty(dto.Questions[1].Options);

        // Секции из LevelTestConfig — только Key + Title.
        Assert.Equal(2, dto.Sections.Count);
        Assert.Equal("csharp-basics", dto.Sections[0].Key);
        Assert.Equal("Основы C#", dto.Sections[0].Title);
        Assert.Equal("csharp-advanced", dto.Sections[1].Key);
        Assert.Equal("Продвинутый C#", dto.Sections[1].Title);
    }

    [Fact]
    public async Task GetActiveLevelTest_NoPublishedLevelTest_Returns404WithTypedError()
    {
        CancellationToken ct = CancellationToken.None;

        // Декой: опубликованный standalone MATERIAL_CHECK квиз не матчится по purpose.
        await SeedQuizAsync(QuizPurpose.MATERIAL_CHECK, publish: true, withConfig: false);

        RemoveAuthentication();
        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/level-test/active", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("quiz.level_test.not_found", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetActiveLevelTest_DraftLevelTestOnly_Returns404()
    {
        CancellationToken ct = CancellationToken.None;
        await SeedQuizAsync(QuizPurpose.LEVEL_TEST, publish: false, withConfig: true);

        RemoveAuthentication();
        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/level-test/active", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetActiveLevelTest_TwoPublished_MostRecentWins()
    {
        CancellationToken ct = CancellationToken.None;
        Guid olderId = await SeedQuizAsync(QuizPurpose.LEVEL_TEST, publish: true, withConfig: true);
        Guid newerId = await SeedQuizAsync(QuizPurpose.LEVEL_TEST, publish: true, withConfig: false);

        // Явный backdate старого квиза — тест проверяет ORDER BY updated_at,
        // а не только tie-break по id.
        await ExecuteInDb(db => db.Database.ExecuteSqlAsync(
            $"UPDATE quizzes SET updated_at = updated_at - interval '1 hour' WHERE id = {olderId}"));

        RemoveAuthentication();
        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/level-test/active", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        LevelTestStudentDto dto = await ReadResultAsync<LevelTestStudentDto>(response);
        Assert.Equal(newerId, dto.Id);
        Assert.NotEqual(olderId, dto.Id);

        // У победителя нет LevelTestConfig → секции пустые (контракт ST-3).
        Assert.Empty(dto.Sections);
    }

    // ===== Helpers =====

    private async Task<Guid> SeedQuizAsync(QuizPurpose purpose, bool publish, bool withConfig)
    {
        Guid quizId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            LevelTestConfig? config = withConfig
                ? LevelTestConfig.Create(
                    [
                        new LevelThreshold(DeveloperLevel.JUNIOR, 0),
                        new LevelThreshold(DeveloperLevel.MIDDLE, 45),
                        new LevelThreshold(DeveloperLevel.SENIOR, 75),
                    ],
                    [
                        new LevelTestSection("csharp-basics", "Основы C#", 1.0m, Guid.NewGuid()),
                        new LevelTestSection("csharp-advanced", "Продвинутый C#", 2.5m, null),
                    ],
                    Guid.NewGuid()).Value
                : null;

            Quiz quiz = Quiz.Create(
                Guid.NewGuid(),
                Title.Create($"Тест уровня {Guid.NewGuid():N}").Value,
                BuildQuestions(),
                purpose: purpose,
                levelTestConfig: config).Value;

            if (publish)
                Assert.True(quiz.Publish().IsSuccess);

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            quizId = quiz.Id;
        });
        return quizId;
    }

    private static List<QuizQuestion> BuildQuestions() =>
    [
        QuizQuestion.Create(
            Guid.NewGuid(),
            QuizQuestionType.SINGLE_CHOICE,
            "Что такое CLR?",
            [
                QuizOption.Create(_optionA, "Среда выполнения").Value,
                QuizOption.Create(_optionB, "Компилятор").Value,
            ],
            [_optionA],
            referenceAnswer: null,
            section: "csharp-basics",
            difficulty: QuestionDifficulty.JUNIOR).Value,
        QuizQuestion.Create(
            Guid.NewGuid(),
            QuizQuestionType.OPEN_TEXT,
            "Объясните boxing и unboxing",
            [],
            [],
            "Эталонное объяснение для грейдера",
            section: "csharp-advanced",
            difficulty: QuestionDifficulty.SENIOR).Value,
    ];
}
