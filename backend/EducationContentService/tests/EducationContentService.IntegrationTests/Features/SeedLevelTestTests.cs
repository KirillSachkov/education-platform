using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.Infrastructure.Postgres;
using EducationContentService.IntegrationTests.Infrastructure;
using EducationContentService.Web.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>
///     Тесты идемпотентного сидера level-test квиза (CLI <c>seed-level-test</c>, #483):
///     ядро <see cref="LevelTestSeeder"/> + контентные инварианты реального seed-файла
///     <c>SeedData/level-test.json</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class SeedLevelTestTests : EducationContentServiceTestsBase
{
    private static readonly string[] _expectedSectionKeys =
    [
        "csharp-runtime", "testing",
    ];

    private static readonly string[] _difficulties = ["JUNIOR", "MIDDLE", "SENIOR"];

    /// <summary>Шкала #528: пороги seed-файла в порядке возрастания.</summary>
    private static readonly (string Level, int MinPercent)[] _expectedThresholds =
    [
        ("PRE_JUNIOR", 0), ("JUNIOR", 25), ("JUNIOR_PLUS", 50),
        ("MIDDLE", 65), ("MIDDLE_PLUS", 78), ("SENIOR", 95),
    ];

    public SeedLevelTestTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task SeedLevelTest_RerunWithoutForce_PreservesOwnerEdits_ForceResetsToSeedFile()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        await SeedCourseAsync(authorId);

        // --- Первый запуск: создание + публикация ---
        Result<LevelTestSeedResult, Error> first = await RunSeederAsync();
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.GetMessage() : null);
        Assert.Equal(LevelTestSeedAction.CREATED, first.Value.Action);
        Assert.Equal(LevelTestSeeder.SeedQuizId, first.Value.QuizId);

        // --- Дрейф: владелец переименовал квиз и заменил вопросы через UI (#487) ---
        await ExecuteInDb(async db =>
        {
            Quiz quiz = await db.Quizzes.FirstAsync(q => q.Id == LevelTestSeeder.SeedQuizId, ct);

            Assert.True(quiz.Update(
                Title.Create("Изменённый вручную заголовок").Value, 50, null, quiz.AccessType).IsSuccess);

            Guid optionA = Guid.NewGuid();
            Guid optionB = Guid.NewGuid();
            QuizQuestion stub = QuizQuestion.Create(
                Guid.NewGuid(),
                QuizQuestionType.SINGLE_CHOICE,
                "Временный вопрос?",
                [QuizOption.Create(optionA, "Да").Value, QuizOption.Create(optionB, "Нет").Value],
                [optionA],
                referenceAnswer: null).Value;
            Assert.True(quiz.UpdateQuestions([stub]).IsSuccess);

            await db.SaveChangesAsync(ct);
        });

        // --- Второй запуск БЕЗ force: квиз не тронут, правки владельца на месте ---
        Result<LevelTestSeedResult, Error> second = await RunSeederAsync();
        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.GetMessage() : null);
        Assert.Equal(LevelTestSeedAction.SKIPPED_EXISTING, second.Value.Action);
        Assert.Equal(LevelTestSeeder.SeedQuizId, second.Value.QuizId);
        Assert.Equal(1, second.Value.QuestionCount);

        await ExecuteInDb(async db =>
        {
            List<Quiz> levelTests = await db.Quizzes.AsNoTracking()
                .Where(q => q.Purpose == QuizPurpose.LEVEL_TEST)
                .ToListAsync(ct);

            Quiz quiz = Assert.Single(levelTests);
            Assert.Equal("Изменённый вручную заголовок", quiz.Title.Value);
            Assert.Equal(50, quiz.PassingScorePercent);
            Assert.Null(quiz.LevelTestConfig);
            QuizQuestion onlyQuestion = Assert.Single(quiz.Questions);
            Assert.Equal("Временный вопрос?", onlyQuestion.Text);
        });

        // --- Третий запуск С force: полная перезапись содержимым seed-файла ---
        Result<LevelTestSeedResult, Error> third = await RunSeederAsync(force: true);
        Assert.True(third.IsSuccess, third.IsFailure ? third.Error.GetMessage() : null);
        Assert.Equal(LevelTestSeedAction.OVERWRITTEN, third.Value.Action);
        Assert.Equal(LevelTestSeeder.SeedQuizId, third.Value.QuizId);

        await ExecuteInDb(async db =>
        {
            List<Quiz> levelTests = await db.Quizzes.AsNoTracking()
                .Where(q => q.Purpose == QuizPurpose.LEVEL_TEST)
                .ToListAsync(ct);

            Quiz quiz = Assert.Single(levelTests);
            Assert.Equal(LevelTestSeeder.SeedQuizId, quiz.Id);
            Assert.Equal(authorId, quiz.AuthorId);
            Assert.Equal(PublicationStatus.PUBLISHED, quiz.Status);
            Assert.Equal(LevelTestSeeder.LoadSeedDocument().Title, quiz.Title.Value);
            // Seed-квиз standalone (#489): на него не ссылается ни один материал,
            // воронка level-test'а публична.
            Assert.False(await db.Materials.AnyAsync(m => m.QuizId == quiz.Id, ct));
            Assert.Equal(AccessType.PUBLIC, quiz.AccessType);
            Assert.Equal(LevelTestSeeder.LoadSeedDocument().Questions.Count, quiz.Questions.Count);
            Assert.NotNull(quiz.LevelTestConfig);
            Assert.Equal(_expectedSectionKeys, quiz.LevelTestConfig!.Sections.Select(s => s.Key));
            Assert.Equal(_expectedThresholds.Length, quiz.LevelTestConfig.LevelThresholds.Count);
        });
    }

    [Fact]
    public async Task SeedLevelTest_WithoutAnyAuthorSource_FailsWithClearError()
    {
        // Пустая БД: ни курсов, ни материалов — primary-автор нерезолвим.
        Result<LevelTestSeedResult, Error> result = await RunSeederAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("quiz.seed.author.unresolved", result.Error.GetMessage(), StringComparison.Ordinal);

        await ExecuteInDb(async db =>
            Assert.False(await db.Quizzes.AnyAsync(CancellationToken.None)));
    }

    [Fact]
    public void SeedDocument_SatisfiesLevelTestContentInvariants()
    {
        LevelTestSeedDocument document = LevelTestSeeder.LoadSeedDocument();

        Assert.False(string.IsNullOrWhiteSpace(document.Title));
        Assert.False(string.IsNullOrWhiteSpace(document.Description));
        Assert.InRange(document.PassingScorePercent, 0, 100);
        Assert.NotEmpty(document.Questions);

        foreach (QuizQuestionRequest question in document.Questions)
        {
            Assert.NotNull(question.Id);
            Assert.NotEqual(Guid.Empty, question.Id!.Value);
            Assert.False(string.IsNullOrWhiteSpace(question.Text));
            Assert.NotNull(question.Section);
            Assert.Contains(question.Section!, _expectedSectionKeys);
            Assert.NotNull(question.Difficulty);
            Assert.Contains(question.Difficulty!, _difficulties);

            if (string.Equals(question.Type, "OPEN_TEXT", StringComparison.Ordinal))
            {
                Assert.False(
                    string.IsNullOrWhiteSpace(question.ReferenceAnswer),
                    $"OPEN_TEXT {question.Id} обязан иметь эталонный ответ для AI-грейдинга");
                Assert.Contains("Критерии оценки", question.ReferenceAnswer!, StringComparison.Ordinal);
                Assert.True(question.Options is null || question.Options.Count == 0);
            }
            else if (string.Equals(question.Type, "EXACT_TEXT", StringComparison.Ordinal))
            {
                // #528: рукописный точный ответ — детерминированная сверка с эталоном.
                Assert.False(
                    string.IsNullOrWhiteSpace(question.ReferenceAnswer),
                    $"EXACT_TEXT {question.Id} обязан иметь эталонный точный ответ");
                Assert.True(question.Options is null || question.Options.Count == 0);
            }
            else
            {
                Assert.NotNull(question.Options);
                Assert.InRange(question.Options!.Count, 2, 10);

                HashSet<Guid> optionIds = question.Options.Select(o => o.Id!.Value).ToHashSet();
                Assert.Equal(question.Options.Count, optionIds.Count);

                Assert.NotNull(question.CorrectOptionIds);
                Assert.True(question.CorrectOptionIds!.Count >= 1);
                Assert.All(question.CorrectOptionIds, correctId => Assert.Contains(correctId, optionIds));

                if (string.Equals(question.Type, "SINGLE_CHOICE", StringComparison.Ordinal))
                    Assert.Single(question.CorrectOptionIds);
            }
        }

        // Каждая демонстрационная секция участвует в расчёте результата.
        foreach (string sectionKey in _expectedSectionKeys)
        {
            int count = document.Questions.Count(
                q => string.Equals(q.Section, sectionKey, StringComparison.Ordinal));
            Assert.True(count >= 1, $"в секции '{sectionKey}' нет вопросов");
        }

        Assert.All(_difficulties, difficulty => Assert.True(CountByDifficulty(document, difficulty) > 0));
        Assert.All(new[] { "SINGLE_CHOICE", "MULTI_CHOICE", "EXACT_TEXT", "OPEN_TEXT" }, type =>
            Assert.Contains(document.Questions, question => question.Type == type));

        // Конфигурация: пороги строго растут; примеры не ссылаются на приватные курсы.
        LevelTestConfigRequest config = document.LevelTestConfig;
        Assert.Equal(_expectedThresholds.Length, config.LevelThresholds.Count);
        foreach ((string level, int minPercent) in _expectedThresholds)
            Assert.Equal(minPercent, ThresholdFor(config, level));
        Assert.True(
            _expectedThresholds.Zip(_expectedThresholds.Skip(1))
                .All(pair => pair.First.MinPercent < pair.Second.MinPercent),
            "пороги уровней обязаны строго расти вдоль шкалы");

        Assert.NotNull(config.Sections);
        Assert.Equal(_expectedSectionKeys, config.Sections!.Select(s => s.Key));
        Assert.All(config.Sections, section =>
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Title));
            Assert.Equal(1.0m, section.Weight);
            Assert.Null(section.RecommendedCourseId);
        });
        Assert.Null(config.FallbackCourseId);

        // Домен принимает контент целиком — прогон через production-мапперы и фабрики.
        Result<List<QuizQuestion>, Error> mapped = QuizQuestionMapper.Map(document.Questions);
        Assert.True(mapped.IsSuccess, mapped.IsFailure ? mapped.Error.GetMessage() : null);
        Assert.Equal(document.Questions.Count, mapped.Value.Count);

        Result<LevelTestConfig?, Error> mappedConfig = QuizLevelTestConfigMapper.Map(document.LevelTestConfig);
        Assert.True(mappedConfig.IsSuccess, mappedConfig.IsFailure ? mappedConfig.Error.GetMessage() : null);
        Assert.NotNull(mappedConfig.Value);
    }

    private static int CountByDifficulty(LevelTestSeedDocument document, string difficulty) =>
        document.Questions.Count(q => string.Equals(q.Difficulty, difficulty, StringComparison.Ordinal));

    private static int ThresholdFor(LevelTestConfigRequest config, string level) =>
        config.LevelThresholds
            .Single(t => string.Equals(t.Level, level, StringComparison.Ordinal))
            .MinPercent;

    private async Task<Result<LevelTestSeedResult, Error>> RunSeederAsync(bool force = false)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        EducationDbContext dbContext = scope.ServiceProvider.GetRequiredService<EducationDbContext>();
        var seeder = new LevelTestSeeder(dbContext, NullLogger<LevelTestSeeder>.Instance);
        return await seeder.SeedAsync(force, CancellationToken.None);
    }

    private async Task SeedCourseAsync(Guid authorId)
    {
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс автора {Guid.NewGuid():N}").Value,
                Description.Create("Описание").Value,
                CourseSlug.Create($"seed-author-course-{Guid.NewGuid():N}").Value,
                SortKey.Initial());
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        });
    }
}
