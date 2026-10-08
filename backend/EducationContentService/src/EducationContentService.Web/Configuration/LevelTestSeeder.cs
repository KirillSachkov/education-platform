using System.Reflection;
using System.Text.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Core.Features.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using SharedKernel;

namespace EducationContentService.Web.Configuration;

/// <summary>
///     Содержимое seed-файла <c>SeedData/level-test.json</c> (embedded resource).
///     Формат вопросов/конфига — те же contract-DTO, что и у author-API
///     (<see cref="QuizQuestionRequest"/> / <see cref="LevelTestConfigRequest"/>),
///     поэтому маппинг и валидация идут через production-мапперы и доменные фабрики.
/// </summary>
/// <param name="Description">
///     Мотивирующее описание теста. У агрегата <see cref="Quiz"/> пока нет поля
///     Description — текст хранится в seed-файле как канонический копирайт воронки
///     (фронт показывает его статически), в БД не персистится.
/// </param>
public sealed record LevelTestSeedDocument(
    string Title,
    string? Description,
    int PassingScorePercent,
    IReadOnlyList<QuizQuestionRequest> Questions,
    LevelTestConfigRequest LevelTestConfig);

/// <summary>Что сделал сидер с seed-квизом.</summary>
public enum LevelTestSeedAction
{
    /// <summary>Квиза не было — создан и опубликован из seed-файла.</summary>
    CREATED,

    /// <summary>Квиз был — перезаписан содержимым seed-файла (только с <c>--force</c>).</summary>
    OVERWRITTEN,

    /// <summary>Квиз был — не тронут (дефолт: правки владельца из UI сохраняются).</summary>
    SKIPPED_EXISTING,
}

/// <summary>Итог сидинга: id квиза, что с ним сделали, сколько вопросов.</summary>
public sealed record LevelTestSeedResult(Guid QuizId, LevelTestSeedAction Action, int QuestionCount);

/// <summary>
///     Идемпотентный сидер level-test квиза «Определи свой уровень .NET-разработчика».
///     Ядро CLI-команды <c>seed-level-test</c> (<see cref="SeedLevelTestCli"/>), вынесено
///     в отдельный класс ради тестируемости.
///     <para>
///     <b>Ключ идемпотентности — фиксированный well-known <see cref="SeedQuizId"/></b>
///     (валидный v7-shaped GUID, суффикс 483 = номер issue). Квиза с этим Id нет →
///     создаём через <see cref="Quiz.Create"/> с явным Id и публикуем. Квиз есть →
///     <b>по умолчанию не трогаем</b> (с #487 владелец редактирует тест из UI —
///     молчаливый overwrite при каждом деплое уничтожал бы его правки) — лог + успешный
///     выход. Полная перезапись содержимым seed-файла (replace title/questions/config +
///     до-publish) — только с явным <c>force: true</c> (CLI-флаг <c>--force</c>).
///     </para>
///     <para>
///     Всё через домен (фабрики + методы агрегата) — никакого raw SQL. Сохранение —
///     прямой <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>: разрешённое
///     исключение для CLI-утилит (см. <c>docs/agents/backend-transactions.md</c>) —
///     Quiz не поднимает domain events и не публикует integration events.
///     </para>
///     <para>
///     Автор квиза — primary-автор платформы, резолвится по данным ECS: автор с
///     наибольшим числом PUBLISHED-курсов (tie-break: всего курсов, затем Id);
///     fallback — автор материалов. Платформа single-tenant, так что на практике
///     это единственный автор. Не резолвится → ошибка (сидить нечем).
///     </para>
/// </summary>
public sealed class LevelTestSeeder
{
    /// <summary>Well-known Id seed-квиза (одинаковый на всех окружениях).</summary>
    public static readonly Guid SeedQuizId = Guid.Parse("0197a000-0000-7000-8000-000000000483");

    private const string SEED_RESOURCE_NAME = "EducationContentService.Web.SeedData.level-test.json";

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly EducationDbContext _dbContext;
    private readonly ILogger<LevelTestSeeder> _logger;

    public LevelTestSeeder(EducationDbContext dbContext, ILogger<LevelTestSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>Читает и десериализует seed-файл из embedded resource сборки.</summary>
    public static LevelTestSeedDocument LoadSeedDocument()
    {
        Assembly assembly = typeof(LevelTestSeeder).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(SEED_RESOURCE_NAME)
            ?? throw new InvalidOperationException($"Embedded resource '{SEED_RESOURCE_NAME}' not found.");

        return JsonSerializer.Deserialize<LevelTestSeedDocument>(stream, _jsonOptions)
            ?? throw new InvalidOperationException($"Embedded resource '{SEED_RESOURCE_NAME}' deserialized to null.");
    }

    public async Task<Result<LevelTestSeedResult, Error>> SeedAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        Quiz? existing = await _dbContext.Quizzes
            .FirstOrDefaultAsync(q => q.Id == SeedQuizId, cancellationToken);

        if (existing is not null && !force)
        {
            _logger.LogInformation(
                "Level-test quiz {QuizId} already exists ({QuestionCount} questions, status {Status}) — " +
                "left untouched (owner edits are preserved). Re-run with --force to overwrite from the seed file",
                existing.Id, existing.Questions.Count, existing.Status);

            return new LevelTestSeedResult(
                existing.Id, LevelTestSeedAction.SKIPPED_EXISTING, existing.Questions.Count);
        }

        LevelTestSeedDocument document = LoadSeedDocument();

        Result<Title, Error> titleResult = Title.Create(document.Title);
        if (titleResult.IsFailure)
            return titleResult.Error;

        Result<List<QuizQuestion>, Error> questionsResult = QuizQuestionMapper.Map(document.Questions);
        if (questionsResult.IsFailure)
            return questionsResult.Error;

        Result<LevelTestConfig?, Error> configResult = QuizLevelTestConfigMapper.Map(document.LevelTestConfig);
        if (configResult.IsFailure)
            return configResult.Error;

        int otherLevelTests = await _dbContext.Quizzes.CountAsync(
            q => q.Purpose == QuizPurpose.LEVEL_TEST && q.Id != SeedQuizId, cancellationToken);
        if (otherLevelTests > 0)
        {
            _logger.LogWarning(
                "Found {Count} other LEVEL_TEST quiz(zes) besides the seed one — " +
                "they are left untouched, review manually if unintended", otherLevelTests);
        }

        return existing is null
            ? await CreateAsync(document, titleResult.Value, questionsResult.Value, configResult.Value, cancellationToken)
            : await OverwriteAsync(existing, document, titleResult.Value, questionsResult.Value, configResult.Value, cancellationToken);
    }

    private async Task<Result<LevelTestSeedResult, Error>> CreateAsync(
        LevelTestSeedDocument document,
        Title title,
        List<QuizQuestion> questions,
        LevelTestConfig? config,
        CancellationToken cancellationToken)
    {
        Guid? authorId = await ResolvePrimaryAuthorIdAsync(cancellationToken);
        if (authorId is null)
        {
            return Error.Failure(
                "quiz.seed.author.unresolved",
                "Не удалось определить primary-автора платформы: в схеме education нет ни курсов, ни материалов. " +
                "Создайте хотя бы один курс от имени автора и повторите seed-level-test.");
        }

        Result<Quiz, Error> quizResult = Quiz.Create(
            authorId.Value,
            title,
            questions,
            document.PassingScorePercent,
            QuizPurpose.LEVEL_TEST,
            config,
            id: SeedQuizId);
        if (quizResult.IsFailure)
            return quizResult.Error;

        Quiz quiz = quizResult.Value;

        UnitResult<Error> publishResult = quiz.Publish();
        if (publishResult.IsFailure)
            return publishResult.Error;

        await _dbContext.Quizzes.AddAsync(quiz, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Level-test quiz {QuizId} seeded (created, published): author {AuthorId}, {QuestionCount} questions",
            quiz.Id, quiz.AuthorId, quiz.Questions.Count);

        return new LevelTestSeedResult(quiz.Id, LevelTestSeedAction.CREATED, quiz.Questions.Count);
    }

    private async Task<Result<LevelTestSeedResult, Error>> OverwriteAsync(
        Quiz quiz,
        LevelTestSeedDocument document,
        Title title,
        List<QuizQuestion> questions,
        LevelTestConfig? config,
        CancellationToken cancellationToken)
    {
        // AccessType сохраняем как есть — воронка level-test'а публична по умолчанию
        // (PUBLIC из factory), а ручную настройку владельца re-seed не перетирает.
        UnitResult<Error> updateResult = quiz.Update(
            title, document.PassingScorePercent, config, quiz.AccessType);
        if (updateResult.IsFailure)
            return updateResult.Error;

        UnitResult<Error> questionsResult = quiz.UpdateQuestions(questions);
        if (questionsResult.IsFailure)
            return questionsResult.Error;

        if (quiz.Status != PublicationStatus.PUBLISHED)
        {
            UnitResult<Error> publishResult = quiz.Publish();
            if (publishResult.IsFailure)
                return publishResult.Error;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Level-test quiz {QuizId} seeded (force-overwritten to latest seed content): {QuestionCount} questions",
            quiz.Id, quiz.Questions.Count);

        return new LevelTestSeedResult(quiz.Id, LevelTestSeedAction.OVERWRITTEN, quiz.Questions.Count);
    }

    private async Task<Guid?> ResolvePrimaryAuthorIdAsync(CancellationToken cancellationToken)
    {
        var topCourseAuthor = await _dbContext.Courses
            .GroupBy(c => c.AuthorId)
            .Select(g => new
            {
                AuthorId = g.Key,
                PublishedCount = g.Count(c => c.Status == PublicationStatus.PUBLISHED),
                TotalCount = g.Count(),
            })
            .OrderByDescending(x => x.PublishedCount)
            .ThenByDescending(x => x.TotalCount)
            .ThenBy(x => x.AuthorId)
            .FirstOrDefaultAsync(cancellationToken);

        if (topCourseAuthor is not null)
            return topCourseAuthor.AuthorId;

        var topMaterialAuthor = await _dbContext.Materials
            .GroupBy(m => m.AuthorId)
            .Select(g => new { AuthorId = g.Key, TotalCount = g.Count() })
            .OrderByDescending(x => x.TotalCount)
            .ThenBy(x => x.AuthorId)
            .FirstOrDefaultAsync(cancellationToken);

        return topMaterialAuthor?.AuthorId;
    }
}
