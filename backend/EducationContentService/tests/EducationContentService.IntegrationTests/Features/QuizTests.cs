using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Materials;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class QuizTests : EducationContentServiceTestsBase
{
    private static readonly Guid _singleOptionA = Guid.NewGuid();
    private static readonly Guid _singleOptionB = Guid.NewGuid();
    private static readonly Guid _multiOptionA = Guid.NewGuid();
    private static readonly Guid _multiOptionB = Guid.NewGuid();
    private static readonly Guid _multiOptionC = Guid.NewGuid();

    public QuizTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ===== Author CRUD (standalone после инверсии #489) =====

    [Fact]
    public async Task QuizAuthorFlow_CreateGetUpdatePublish_Roundtrip()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAs(authorId, "platform-author");

        // --- Create (standalone, с собственным AccessType) ---
        var createRequest = new CreateQuizRequest(
            "Квиз по основам",
            BuildValidQuestionRequests(),
            PassingScorePercent: 80,
            AccessType: "REGISTERED");

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/quizzes", createRequest, ct);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Guid quizId = await ReadResultAsync<Guid>(createResponse);

        // --- Get (авторский view ВКЛЮЧАЕТ ответы) ---
        HttpResponseMessage getResponse = await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        QuizAuthorDto authorView = await ReadResultAsync<QuizAuthorDto>(getResponse);

        Assert.Equal(quizId, authorView.Id);
        Assert.Equal("Квиз по основам", authorView.Title);
        Assert.Equal(80, authorView.PassingScorePercent);
        Assert.Equal("DRAFT", authorView.Status);
        Assert.Equal("REGISTERED", authorView.AccessType);
        Assert.Equal("MATERIAL_CHECK", authorView.Purpose);
        Assert.Null(authorView.LevelTestConfig);
        Assert.Equal(3, authorView.Questions.Count);

        QuizQuestionAuthorDto single = authorView.Questions[0];
        Assert.Equal("SINGLE_CHOICE", single.Type);
        Assert.Equal(new[] { _singleOptionA }, single.CorrectOptionIds);

        QuizQuestionAuthorDto multi = authorView.Questions[1];
        Assert.Equal("MULTI_CHOICE", multi.Type);
        Assert.Equal(2, multi.CorrectOptionIds.Count);

        QuizQuestionAuthorDto open = authorView.Questions[2];
        Assert.Equal("OPEN_TEXT", open.Type);
        Assert.Equal("Эталонное объяснение", open.ReferenceAnswer);

        // --- Update (replace всего набора; AccessType=null ⇒ не меняется) ---
        Guid newOptionA = Guid.NewGuid();
        Guid newOptionB = Guid.NewGuid();
        var updateRequest = new UpdateQuizRequest(
            "Квиз по основам v2",
            [
                new QuizQuestionRequest(
                    null,
                    "SINGLE_CHOICE",
                    "Новый единственный вопрос?",
                    [new QuizOptionRequest(newOptionA, "Да"), new QuizOptionRequest(newOptionB, "Нет")],
                    [newOptionB]),
            ],
            PassingScorePercent: 50);

        HttpResponseMessage updateResponse = await AppHttpClient.PutAsJsonAsync($"/quizzes/{quizId}", updateRequest, ct);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        QuizAuthorDto updatedView = await ReadResultAsync<QuizAuthorDto>(
            await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct));
        Assert.Equal("Квиз по основам v2", updatedView.Title);
        Assert.Equal(50, updatedView.PassingScorePercent);
        Assert.Equal("REGISTERED", updatedView.AccessType);
        QuizQuestionAuthorDto replaced = Assert.Single(updatedView.Questions);
        Assert.Equal("Новый единственный вопрос?", replaced.Text);
        Assert.Equal(new[] { newOptionB }, replaced.CorrectOptionIds);

        // --- Update с явным AccessType — меняется ---
        var accessUpdateRequest = updateRequest with { AccessType = "ENROLLED" };
        HttpResponseMessage accessUpdateResponse =
            await AppHttpClient.PutAsJsonAsync($"/quizzes/{quizId}", accessUpdateRequest, ct);
        Assert.Equal(HttpStatusCode.OK, accessUpdateResponse.StatusCode);

        QuizAuthorDto accessUpdatedView = await ReadResultAsync<QuizAuthorDto>(
            await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct));
        Assert.Equal("ENROLLED", accessUpdatedView.AccessType);

        // --- Publish ---
        HttpResponseMessage publishResponse = await AppHttpClient.PostAsync($"/quizzes/{quizId}/publish", null, ct);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        await ExecuteInDb(async db =>
        {
            Quiz quiz = await db.Quizzes.AsNoTracking().FirstAsync(q => q.Id == quizId, ct);
            Assert.Equal(PublicationStatus.PUBLISHED, quiz.Status);
            Assert.Equal(AccessType.ENROLLED, quiz.AccessType);
            Assert.Single(quiz.Questions);
        });

        // --- Delete ---
        HttpResponseMessage deleteResponse = await AppHttpClient.DeleteAsync($"/quizzes/{quizId}", ct);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        await ExecuteInDb(async db =>
            Assert.False(await db.Quizzes.AnyAsync(q => q.Id == quizId, ct)));
    }

    [Fact]
    public async Task CreateQuiz_WithoutAccessType_DefaultsToPublic()
    {
        CancellationToken ct = CancellationToken.None;
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync(
            "/quizzes", new CreateQuizRequest("Квиз без accessType"), ct);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Guid quizId = await ReadResultAsync<Guid>(createResponse);

        QuizAuthorDto authorView = await ReadResultAsync<QuizAuthorDto>(
            await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct));
        Assert.Equal("PUBLIC", authorView.AccessType);
    }

    [Fact]
    public async Task CreateQuiz_AdminWithAuthorIdOverride_SetsAuthor()
    {
        CancellationToken ct = CancellationToken.None;
        Guid targetAuthorId = Guid.NewGuid();
        AuthenticateAs(Guid.NewGuid(), "platform-admin");

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync(
            "/quizzes",
            new CreateQuizRequest("Квиз с override автора", AuthorId: targetAuthorId),
            ct);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Guid quizId = await ReadResultAsync<Guid>(createResponse);

        QuizAuthorDto authorView = await ReadResultAsync<QuizAuthorDto>(
            await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct));
        Assert.Equal(targetAuthorId, authorView.AuthorId);
    }

    [Fact]
    public async Task CreateQuiz_NonAdminWithAuthorIdOverride_IgnoresOverride()
    {
        CancellationToken ct = CancellationToken.None;
        Guid callerId = Guid.NewGuid();
        AuthenticateAs(callerId, "platform-author");

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync(
            "/quizzes",
            new CreateQuizRequest("Квиз с игнорируемым override", AuthorId: Guid.NewGuid()),
            ct);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Guid quizId = await ReadResultAsync<Guid>(createResponse);

        QuizAuthorDto authorView = await ReadResultAsync<QuizAuthorDto>(
            await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct));
        Assert.Equal(callerId, authorView.AuthorId);
    }

    [Fact]
    public async Task PublishQuiz_WithoutQuestions_Returns400()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAs(authorId, "platform-author");

        var createRequest = new CreateQuizRequest("Пустой квиз");
        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/quizzes", createRequest, ct);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Guid quizId = await ReadResultAsync<Guid>(createResponse);

        HttpResponseMessage publishResponse = await AppHttpClient.PostAsync($"/quizzes/{quizId}/publish", null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, publishResponse.StatusCode);
        string payload = await publishResponse.Content.ReadAsStringAsync(ct);
        Assert.Contains("quiz.publish.empty", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdatePublishedQuiz_WithEmptyQuestions_Returns400AndPreservesQuestions()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: true);
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/quizzes/{quizId}",
            new UpdateQuizRequest("Пустой опубликованный квиз", Questions: []),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("quiz.publish.empty", payload, StringComparison.Ordinal);
        await ExecuteInDb(async db =>
        {
            Quiz persisted = await db.Quizzes.AsNoTracking().SingleAsync(q => q.Id == quizId, ct);
            Assert.Equal(PublicationStatus.PUBLISHED, persisted.Status);
            Assert.Equal(3, persisted.Questions.Count);
        });
    }

    [Fact]
    public async Task CreateQuiz_SingleChoiceWithTwoCorrectIds_Returns400()
    {
        CancellationToken ct = CancellationToken.None;
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        Guid optionA = Guid.NewGuid();
        Guid optionB = Guid.NewGuid();
        var request = new CreateQuizRequest(
            "Невалидный single-choice",
            Questions:
            [
                new QuizQuestionRequest(
                    null,
                    "SINGLE_CHOICE",
                    "Вопрос с двумя правильными?",
                    [new QuizOptionRequest(optionA, "А"), new QuizOptionRequest(optionB, "Б")],
                    [optionA, optionB]),
            ]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/quizzes", request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("quiz.question.correct_options.invalid", payload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(11)]
    public async Task CreateQuiz_ChoiceOptionsCountOutOfBounds_Returns400(int optionsCount)
    {
        CancellationToken ct = CancellationToken.None;
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        List<QuizOptionRequest> options = Enumerable.Range(1, optionsCount)
            .Select(i => new QuizOptionRequest(Guid.NewGuid(), $"Вариант {i}"))
            .ToList();

        var request = new CreateQuizRequest(
            $"Квиз с {optionsCount} вариантами",
            Questions:
            [
                new QuizQuestionRequest(
                    null,
                    "SINGLE_CHOICE",
                    "Сколько вариантов нормально?",
                    options,
                    [options[0].Id!.Value]),
            ]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/quizzes", request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("quiz.question.options.count.invalid", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetAuthorQuiz_AsNonOwner_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: false);

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ===== Explanation round-trip + no-leak (#561) =====

    [Fact]
    public async Task CreateQuiz_WithExplanation_RoundtripsViaAuthorGet_AndNeverLeaksToStudent()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        const string explanation = "Среда выполнения — это CLR, а не компилятор";
        AuthenticateAs(authorId, "platform-author");

        Guid optionA = Guid.NewGuid();
        Guid optionB = Guid.NewGuid();
        var createRequest = new CreateQuizRequest(
            "Квиз с пояснением",
            Questions:
            [
                new QuizQuestionRequest(
                    null,
                    "SINGLE_CHOICE",
                    "Что такое CLR?",
                    [new QuizOptionRequest(optionA, "Среда выполнения"), new QuizOptionRequest(optionB, "Компилятор")],
                    [optionA],
                    Explanation: explanation),
                // Второй вопрос без пояснения — null проходит насквозь.
                new QuizQuestionRequest(
                    null,
                    "OPEN_TEXT",
                    "Объясните boxing",
                    null,
                    null,
                    "Эталон"),
            ],
            AccessType: "PUBLIC");

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/quizzes", createRequest, ct);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Guid quizId = await ReadResultAsync<Guid>(createResponse);

        // --- Author GET: Explanation round-trip'ится в правильной позиции ---
        QuizAuthorDto authorView = await ReadResultAsync<QuizAuthorDto>(
            await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct));
        Assert.Equal(2, authorView.Questions.Count);
        Assert.Equal(explanation, authorView.Questions[0].Explanation);
        Assert.Null(authorView.Questions[1].Explanation);

        // --- Publish + bind to material, проверяем студенческую проекцию ---
        HttpResponseMessage publishResponse = await AppHttpClient.PostAsync($"/quizzes/{quizId}/publish", null, ct);
        Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

        Guid materialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        EntitlementChecker.GrantAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage studentResponse = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);
        Assert.Equal(HttpStatusCode.OK, studentResponse.StatusCode);

        // Студенческая проекция НЕ содержит пояснение — ни под каким ключом, ни значением.
        string rawJson = await studentResponse.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("explanation", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(explanation, rawJson, StringComparison.Ordinal);

        // Контрактная проверка через JsonDocument: свойство "explanation" физически отсутствует.
        using JsonDocument document = JsonDocument.Parse(rawJson);
        JsonElement questions = document.RootElement.GetProperty("result").GetProperty("questions");
        foreach (JsonElement question in questions.EnumerateArray())
        {
            Assert.False(
                question.TryGetProperty("explanation", out _),
                "Студенческая проекция квиза не должна содержать поле explanation");
        }
    }

    // ===== Привязка квиза к материалу (инверсия #489: materials.quiz_id) =====

    [Fact]
    public async Task BindQuizToMaterial_ViaPatch_ResolvesThroughStudentAuthorAndDetailEndpoints()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        string materialTitle = $"Материал {Guid.NewGuid():N}";
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED, materialTitle);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");

        // PATCH материала с QuizId — единственный путь привязки после инверсии.
        HttpResponseMessage patchResponse = await PatchAsJsonAsync(
            $"/materials/{materialId}",
            new UpdateMaterialRequest(
                materialTitle, "# Тело материала", "ARTICLE", "ENROLLED", QuizId: quizId));
        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.AsNoTracking().FirstAsync(m => m.Id == materialId, ct);
            Assert.Equal(quizId, material.QuizId);
        });

        // Авторский rediscovery резолвится через materials.quiz_id.
        QuizAuthorDto authorView = await ReadResultAsync<QuizAuthorDto>(
            await AppHttpClient.GetAsync($"/quizzes/by-material/{materialId}", ct));
        Assert.Equal(quizId, authorView.Id);

        // MaterialDetailDto несёт QuizId (фронту ST-15/16).
        MaterialDetailDto detail = await ReadResultAsync<MaterialDetailDto>(
            await AppHttpClient.GetAsync($"/materials/{materialId}/detail", ct));
        Assert.Equal(quizId, detail.QuizId);

        // Студенческий эндпоинт отдаёт PUBLISHED-квиз по той же ссылке.
        EntitlementChecker.GrantAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");
        QuizStudentDto studentView = await ReadResultAsync<QuizStudentDto>(
            await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct));
        Assert.Equal(quizId, studentView.Id);
        Assert.Equal(materialId, studentView.MaterialId);
    }

    [Fact]
    public async Task QuizReusedByTwoMaterials_BothResolveIt()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid firstMaterialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        Guid secondMaterialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);

        await BindQuizToMaterialInDbAsync(firstMaterialId, quizId);
        await BindQuizToMaterialInDbAsync(secondMaterialId, quizId);

        EntitlementChecker.GrantAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        QuizStudentDto firstView = await ReadResultAsync<QuizStudentDto>(
            await AppHttpClient.GetAsync($"/materials/{firstMaterialId}/quiz", ct));
        QuizStudentDto secondView = await ReadResultAsync<QuizStudentDto>(
            await AppHttpClient.GetAsync($"/materials/{secondMaterialId}/quiz", ct));

        Assert.Equal(quizId, firstView.Id);
        Assert.Equal(quizId, secondView.Id);
        Assert.Equal(firstMaterialId, firstView.MaterialId);
        Assert.Equal(secondMaterialId, secondView.MaterialId);
    }

    [Fact]
    public async Task UpdateMaterial_WithForeignQuiz_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        string materialTitle = $"Материал {Guid.NewGuid():N}";
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC, materialTitle);
        Guid foreignQuizId = await SeedQuizAsync(Guid.NewGuid(), publish: true);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await PatchAsJsonAsync(
            $"/materials/{materialId}",
            new UpdateMaterialRequest(
                materialTitle, "# Тело материала", "ARTICLE", "PUBLIC", QuizId: foreignQuizId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.AsNoTracking().FirstAsync(m => m.Id == materialId, ct);
            Assert.Null(material.QuizId);
        });
    }

    [Fact]
    public async Task UpdateMaterial_WithMissingQuiz_Returns404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        string materialTitle = $"Материал {Guid.NewGuid():N}";
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC, materialTitle);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await PatchAsJsonAsync(
            $"/materials/{materialId}",
            new UpdateMaterialRequest(
                materialTitle, "# Тело материала", "ARTICLE", "PUBLIC", QuizId: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("quiz.not.found", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateMaterial_QuizIdNull_DetachesButQuizSurvives()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        string materialTitle = $"Материал {Guid.NewGuid():N}";
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC, materialTitle);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await PatchAsJsonAsync(
            $"/materials/{materialId}",
            new UpdateMaterialRequest(
                materialTitle, "# Тело материала", "ARTICLE", "PUBLIC", QuizId: null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.AsNoTracking().FirstAsync(m => m.Id == materialId, ct);
            Assert.Null(material.QuizId);
            Assert.True(await db.Quizzes.AnyAsync(q => q.Id == quizId, ct));
        });
    }

    // ===== DeleteQuiz каскад (#489): обнуляет materials.quiz_id + чистит course_quizzes =====

    [Fact]
    public async Task DeleteQuiz_NullifiesMaterialRefsAndCourseBindings()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid firstMaterialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        Guid secondMaterialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);
        Guid otherQuizId = await SeedQuizAsync(authorId, publish: true);

        await BindQuizToMaterialInDbAsync(firstMaterialId, quizId);
        await BindQuizToMaterialInDbAsync(secondMaterialId, quizId);

        Guid courseId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {Guid.NewGuid():N}").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create($"course-{Guid.CreateVersion7():N}").Value,
                SortKey.Initial());
            db.Courses.Add(course);
            courseId = course.Id;

            db.CourseQuizzes.Add(new CourseQuiz(courseId, quizId, SortKey.Initial()));
            // Привязка другого квиза — не должна быть задета каскадом.
            db.CourseQuizzes.Add(new CourseQuiz(courseId, otherQuizId, SortKey.Initial()));
            await db.SaveChangesAsync(ct);
        });

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/quizzes/{quizId}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            Assert.False(await db.Quizzes.AnyAsync(q => q.Id == quizId, ct));

            // Ссылки материалов обнулены, материалы живы.
            Material first = await db.Materials.AsNoTracking().FirstAsync(m => m.Id == firstMaterialId, ct);
            Material second = await db.Materials.AsNoTracking().FirstAsync(m => m.Id == secondMaterialId, ct);
            Assert.Null(first.QuizId);
            Assert.Null(second.QuizId);

            // course_quizzes удалённого квиза снесены; привязка другого квиза не задета.
            Assert.False(await db.CourseQuizzes.AnyAsync(cq => cq.QuizId == quizId, ct));
            Assert.True(await db.CourseQuizzes.AnyAsync(
                cq => cq.QuizId == otherQuizId && cq.CourseId == courseId, ct));
        });
    }

    // ===== Level-test (#476) =====

    [Fact]
    public async Task CreateLevelTestQuiz_WithSectionsDifficultiesAndConfig_RoundtripsViaAuthorGet()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAs(authorId, "platform-author");

        Guid recommendedCourseId = Guid.NewGuid();
        Guid fallbackCourseId = Guid.NewGuid();

        var createRequest = new CreateQuizRequest(
            "Входной тест уровня",
            Questions:
            [
                new QuizQuestionRequest(
                    null,
                    "SINGLE_CHOICE",
                    "Что такое CLR?",
                    [new QuizOptionRequest(_singleOptionA, "Среда выполнения"), new QuizOptionRequest(_singleOptionB, "Компилятор")],
                    [_singleOptionA],
                    Section: "csharp-basics",
                    Difficulty: "JUNIOR"),
                new QuizQuestionRequest(
                    null,
                    "OPEN_TEXT",
                    "Объясните boxing/unboxing",
                    null,
                    null,
                    "Эталонное объяснение",
                    Section: "csharp-advanced",
                    Difficulty: "SENIOR"),
            ],
            PassingScorePercent: 70,
            Purpose: "LEVEL_TEST",
            LevelTestConfig: new LevelTestConfigRequest(
                [
                    new LevelThresholdRequest("JUNIOR", 0),
                    new LevelThresholdRequest("MIDDLE", 45),
                    new LevelThresholdRequest("SENIOR", 75),
                ],
                [
                    new LevelTestSectionRequest("csharp-basics", "Основы C#", 1.0m, recommendedCourseId),
                    new LevelTestSectionRequest("csharp-advanced", "Продвинутый C#", 2.5m),
                ],
                fallbackCourseId));

        HttpResponseMessage createResponse = await AppHttpClient.PostAsJsonAsync("/quizzes", createRequest, ct);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Guid quizId = await ReadResultAsync<Guid>(createResponse);

        // GET перечитывает агрегат из БД — проверяет, что обе JSONB-колонки
        // (questions c section/difficulty + level_test_config) переживают round-trip.
        QuizAuthorDto authorView = await ReadResultAsync<QuizAuthorDto>(
            await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct));

        Assert.Equal("LEVEL_TEST", authorView.Purpose);
        // Воронка level-test'а публична — AccessType-дефолт PUBLIC.
        Assert.Equal("PUBLIC", authorView.AccessType);

        Assert.Equal(2, authorView.Questions.Count);
        Assert.Equal("csharp-basics", authorView.Questions[0].Section);
        Assert.Equal("JUNIOR", authorView.Questions[0].Difficulty);
        Assert.Equal("csharp-advanced", authorView.Questions[1].Section);
        Assert.Equal("SENIOR", authorView.Questions[1].Difficulty);

        Assert.NotNull(authorView.LevelTestConfig);
        LevelTestConfigDto config = authorView.LevelTestConfig!;
        Assert.Equal(3, config.LevelThresholds.Count);
        Assert.Equal("JUNIOR", config.LevelThresholds[0].Level);
        Assert.Equal(0, config.LevelThresholds[0].MinPercent);
        Assert.Equal("MIDDLE", config.LevelThresholds[1].Level);
        Assert.Equal(45, config.LevelThresholds[1].MinPercent);
        Assert.Equal("SENIOR", config.LevelThresholds[2].Level);
        Assert.Equal(75, config.LevelThresholds[2].MinPercent);

        Assert.Equal(2, config.Sections.Count);
        Assert.Equal("csharp-basics", config.Sections[0].Key);
        Assert.Equal("Основы C#", config.Sections[0].Title);
        Assert.Equal(1.0m, config.Sections[0].Weight);
        Assert.Equal(recommendedCourseId, config.Sections[0].RecommendedCourseId);
        Assert.Equal("csharp-advanced", config.Sections[1].Key);
        Assert.Equal(2.5m, config.Sections[1].Weight);
        Assert.Null(config.Sections[1].RecommendedCourseId);
        Assert.Equal(fallbackCourseId, config.FallbackCourseId);

        // --- Update: автор правит конфиг (replace целиком), purpose immutable ---
        var updateRequest = new UpdateQuizRequest(
            "Входной тест уровня v2",
            createRequest.Questions,
            PassingScorePercent: 70,
            LevelTestConfig: new LevelTestConfigRequest(
                [new LevelThresholdRequest("JUNIOR", 0), new LevelThresholdRequest("SENIOR", 80)],
                [new LevelTestSectionRequest("csharp-basics", "Основы C#")]));

        HttpResponseMessage updateResponse = await AppHttpClient.PutAsJsonAsync($"/quizzes/{quizId}", updateRequest, ct);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        QuizAuthorDto updatedView = await ReadResultAsync<QuizAuthorDto>(
            await AppHttpClient.GetAsync($"/quizzes/{quizId}", ct));

        Assert.Equal("LEVEL_TEST", updatedView.Purpose);
        Assert.NotNull(updatedView.LevelTestConfig);
        Assert.Equal(2, updatedView.LevelTestConfig!.LevelThresholds.Count);
        Assert.Equal("SENIOR", updatedView.LevelTestConfig.LevelThresholds[1].Level);
        Assert.Equal(80, updatedView.LevelTestConfig.LevelThresholds[1].MinPercent);
        LevelTestSectionDto updatedSection = Assert.Single(updatedView.LevelTestConfig.Sections);
        Assert.Equal(1.0m, updatedSection.Weight);
        Assert.Null(updatedView.LevelTestConfig.FallbackCourseId);
    }

    // ===== Author level-test rediscovery (#487) =====

    [Fact]
    public async Task GetMyLevelTests_AsOwner_ReturnsOwnDraftWithAnswersAndConfig()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid recommendedCourseId = Guid.NewGuid();

        LevelTestConfig config = LevelTestConfig.Create(
            [
                new LevelThreshold(DeveloperLevel.JUNIOR, 0),
                new LevelThreshold(DeveloperLevel.MIDDLE, 45),
                new LevelThreshold(DeveloperLevel.SENIOR, 75),
            ],
            [new LevelTestSection("csharp-basics", "Основы C#", 1.0m, recommendedCourseId)],
            fallbackCourseId: null).Value;

        Guid quizId = await SeedQuizAsync(
            authorId, publish: false,
            purpose: QuizPurpose.LEVEL_TEST, levelTestConfig: config);

        // Чужой level-test не должен попасть в выдачу владельца.
        await SeedQuizAsync(
            Guid.NewGuid(), publish: true, purpose: QuizPurpose.LEVEL_TEST);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/level-test/mine", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<QuizAuthorDto> levelTests = await ReadResultAsync<List<QuizAuthorDto>>(response);

        QuizAuthorDto quiz = Assert.Single(levelTests);
        Assert.Equal(quizId, quiz.Id);
        Assert.Equal(authorId, quiz.AuthorId);
        Assert.Equal("DRAFT", quiz.Status);
        Assert.Equal("LEVEL_TEST", quiz.Purpose);

        // Полная авторская проекция: ответы + section/difficulty на вопросах.
        Assert.Equal(3, quiz.Questions.Count);
        Assert.Equal(new[] { _singleOptionA }, quiz.Questions[0].CorrectOptionIds);
        Assert.Equal("csharp-basics", quiz.Questions[0].Section);
        Assert.Equal("JUNIOR", quiz.Questions[0].Difficulty);
        Assert.Equal("Эталонный ответ для грейдера", quiz.Questions[2].ReferenceAnswer);

        // LevelTestConfig целиком (пороги/секции с весами и рекомендациями).
        Assert.NotNull(quiz.LevelTestConfig);
        Assert.Equal(3, quiz.LevelTestConfig!.LevelThresholds.Count);
        LevelTestSectionDto section = Assert.Single(quiz.LevelTestConfig.Sections);
        Assert.Equal("csharp-basics", section.Key);
        Assert.Equal(recommendedCourseId, section.RecommendedCourseId);
    }

    [Fact]
    public async Task GetMyLevelTests_NoLevelTests_ReturnsEmptyList()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        // MATERIAL_CHECK-квиз автора не должен попасть в level-test выдачу.
        await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/level-test/mine", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<QuizAuthorDto> levelTests = await ReadResultAsync<List<QuizAuthorDto>>(response);
        Assert.Empty(levelTests);
    }

    [Fact]
    public async Task GetMyLevelTests_AsAdmin_ReturnsAllAuthorsLevelTests_NewestFirst()
    {
        CancellationToken ct = CancellationToken.None;
        Guid olderQuizId = await SeedQuizAsync(
            Guid.NewGuid(), publish: true, purpose: QuizPurpose.LEVEL_TEST);
        Guid newerQuizId = await SeedQuizAsync(
            Guid.NewGuid(), publish: false, purpose: QuizPurpose.LEVEL_TEST);

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/level-test/mine", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<QuizAuthorDto> levelTests = await ReadResultAsync<List<QuizAuthorDto>>(response);

        Assert.Equal(2, levelTests.Count);
        Assert.Equal(newerQuizId, levelTests[0].Id);
        Assert.Equal(olderQuizId, levelTests[1].Id);
    }

    [Fact]
    public async Task GetMyLevelTests_Anonymous_Returns401()
    {
        CancellationToken ct = CancellationToken.None;
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/level-test/mine", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ===== Author rediscovery by material (#471) =====

    [Fact]
    public async Task GetAuthorQuizByMaterial_DraftQuiz_AsOwner_Returns200WithAnswers()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED);
        Guid quizId = await SeedQuizAsync(authorId, publish: false);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/by-material/{materialId}", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizAuthorDto authorView = await ReadResultAsync<QuizAuthorDto>(response);

        Assert.Equal(quizId, authorView.Id);
        Assert.Equal("DRAFT", authorView.Status);
        Assert.Equal(3, authorView.Questions.Count);
        Assert.Equal(new[] { _singleOptionA }, authorView.Questions[0].CorrectOptionIds);
        Assert.Equal("Эталонный ответ для грейдера", authorView.Questions[2].ReferenceAnswer);
    }

    [Fact]
    public async Task GetAuthorQuizByMaterial_AsNonOwner_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED);
        Guid quizId = await SeedQuizAsync(authorId, publish: false);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        AuthenticateAs(Guid.NewGuid(), "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/by-material/{materialId}", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuthorQuizByMaterial_NoQuiz_Returns404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/by-material/{materialId}", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ===== Student endpoint =====

    [Fact]
    public async Task GetMaterialQuiz_PublishedQuiz_Returns200_AndNeverLeaksAnswers()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        EntitlementChecker.GrantAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // String-level контракт: ответы не должны присутствовать в JSON ни под каким ключом.
        string rawJson = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("correctOptionIds", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("referenceAnswer", rawJson, StringComparison.OrdinalIgnoreCase);

        QuizStudentDto studentView = await ReadResultAsync<QuizStudentDto>(response);
        Assert.Equal(quizId, studentView.Id);
        Assert.Equal(materialId, studentView.MaterialId);
        Assert.Equal(3, studentView.Questions.Count);
        Assert.Equal(2, studentView.Questions[0].Options.Count);
        Assert.Empty(studentView.Questions[2].Options);

        // Студенческая проекция несёт section/difficulty (прогресс по секциям на фронте).
        Assert.Equal("csharp-basics", studentView.Questions[0].Section);
        Assert.Equal("JUNIOR", studentView.Questions[0].Difficulty);
        Assert.Null(studentView.Questions[1].Section);
        Assert.Null(studentView.Questions[1].Difficulty);
    }

    [Fact]
    public async Task GetMaterialQuiz_DraftQuiz_Returns404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED);
        Guid quizId = await SeedQuizAsync(authorId, publish: false);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        EntitlementChecker.GrantAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialQuiz_NoQuiz_Returns404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid materialId = await SeedMaterialAsync(Guid.NewGuid(), AccessType.PUBLIC);

        EntitlementChecker.GrantAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialQuiz_GatedMaterialDenied_Returns403ForAuthenticated()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        EntitlementChecker.DenyAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialQuiz_GatedMaterialAnonymous_Returns401()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        EntitlementChecker.DenyAll();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialQuiz_AnonymousPublicMaterial_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        // DenyAll доказывает, что PUBLIC short-circuit идёт по данным БД и не зовёт
        // entitlement checker — зеркало поведения GetMaterialDetail.
        EntitlementChecker.DenyAll();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialQuiz_AsMaterialAuthor_DeniedEntitlements_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        // Автор материала проходит по ownership short-circuit даже при DenyAll —
        // зеркало GetMaterialDetail.
        EntitlementChecker.DenyAll();
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ===== Internal answer-key =====

    [Fact]
    public async Task GetQuizAnswerKey_AsAdmin_ReturnsCorrectOptionIds()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.ENROLLED);
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/quizzes/{quizId}/answer-key", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizAnswerKeyDto answerKey = await ReadResultAsync<QuizAnswerKeyDto>(response);

        Assert.Equal(quizId, answerKey.QuizId);
        Assert.Equal("MATERIAL_CHECK", answerKey.Purpose);
        Assert.Equal("ENROLLED", answerKey.AccessType);
        Assert.Null(answerKey.LevelTestConfig);
        Assert.Equal(70, answerKey.PassingScorePercent);
        Assert.Equal(3, answerKey.Questions.Count);
        Assert.Equal(new[] { _singleOptionA }, answerKey.Questions[0].CorrectOptionIds);
        Assert.Null(answerKey.Questions[0].ReferenceAnswer);
        Assert.Equal("Что такое CLR?", answerKey.Questions[0].Text);
        Assert.Equal("csharp-basics", answerKey.Questions[0].Section);
        Assert.Equal("JUNIOR", answerKey.Questions[0].Difficulty);
        Assert.Equal(2, answerKey.Questions[1].CorrectOptionIds.Count);
        Assert.Contains(_multiOptionA, answerKey.Questions[1].CorrectOptionIds);
        Assert.Contains(_multiOptionC, answerKey.Questions[1].CorrectOptionIds);
        Assert.Null(answerKey.Questions[1].Section);
        Assert.Null(answerKey.Questions[1].Difficulty);
        Assert.Empty(answerKey.Questions[2].CorrectOptionIds);
        // Text — для AI-грейдинга открытых ответов в ProgressService (ST-5, #480).
        Assert.Equal("Объясните difference между class и struct", answerKey.Questions[2].Text);
        Assert.Equal("Эталонный ответ для грейдера", answerKey.Questions[2].ReferenceAnswer);
    }

    [Fact]
    public async Task GetQuizAnswerKey_LevelTestQuiz_ReturnsPurposeQuestionMetadataAndFullConfig()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid recommendedCourseId = Guid.NewGuid();
        Guid fallbackCourseId = Guid.NewGuid();

        LevelTestConfig config = LevelTestConfig.Create(
            [
                new LevelThreshold(DeveloperLevel.JUNIOR, 0),
                new LevelThreshold(DeveloperLevel.MIDDLE, 45),
                new LevelThreshold(DeveloperLevel.SENIOR, 75),
            ],
            [
                new LevelTestSection("csharp-basics", "Основы C#", 1.0m, recommendedCourseId),
                new LevelTestSection("csharp-advanced", "Продвинутый C#", 2.5m, null),
            ],
            fallbackCourseId).Value;

        Guid quizId = await SeedQuizAsync(
            authorId,
            publish: true,
            purpose: QuizPurpose.LEVEL_TEST,
            levelTestConfig: config);

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/quizzes/{quizId}/answer-key", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Точный casing JSON-ключей — контракт для S2S-клиента (BaseHttpClient
        // десериализует web-defaults camelCase; ProgressService ST-4 строит на этом
        // секционный скоринг). Сервер пишет компактный camelCase JSON.
        string rawJson = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("\"purpose\":\"LEVEL_TEST\"", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"accessType\":\"PUBLIC\"", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"section\":\"csharp-basics\"", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"difficulty\":\"JUNIOR\"", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"levelTestConfig\":{", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"levelThresholds\":[", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"level\":\"MIDDLE\"", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"minPercent\":45", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"sections\":[", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"key\":\"csharp-advanced\"", rawJson, StringComparison.Ordinal);
        Assert.Contains("\"weight\":2.5", rawJson, StringComparison.Ordinal);
        Assert.Contains($"\"recommendedCourseId\":\"{recommendedCourseId}\"", rawJson, StringComparison.Ordinal);
        Assert.Contains($"\"fallbackCourseId\":\"{fallbackCourseId}\"", rawJson, StringComparison.Ordinal);

        QuizAnswerKeyDto answerKey = await ReadResultAsync<QuizAnswerKeyDto>(response);

        Assert.Equal(quizId, answerKey.QuizId);
        Assert.Equal("LEVEL_TEST", answerKey.Purpose);

        // Section/Difficulty per-вопрос: Q1 размечен, Q2/Q3 — нет (null проходит насквозь).
        Assert.Equal(3, answerKey.Questions.Count);
        Assert.Equal("Что такое CLR?", answerKey.Questions[0].Text);
        Assert.Equal("csharp-basics", answerKey.Questions[0].Section);
        Assert.Equal("JUNIOR", answerKey.Questions[0].Difficulty);
        Assert.Equal(new[] { _singleOptionA }, answerKey.Questions[0].CorrectOptionIds);
        Assert.Null(answerKey.Questions[1].Section);
        Assert.Null(answerKey.Questions[1].Difficulty);
        Assert.Equal("Эталонный ответ для грейдера", answerKey.Questions[2].ReferenceAnswer);

        // Полный LevelTestConfig: пороги + секции с весами/рекомендациями + fallback.
        Assert.NotNull(answerKey.LevelTestConfig);
        LevelTestConfigDto configDto = answerKey.LevelTestConfig!;
        Assert.Equal(3, configDto.LevelThresholds.Count);
        Assert.Equal("JUNIOR", configDto.LevelThresholds[0].Level);
        Assert.Equal(0, configDto.LevelThresholds[0].MinPercent);
        Assert.Equal("MIDDLE", configDto.LevelThresholds[1].Level);
        Assert.Equal(45, configDto.LevelThresholds[1].MinPercent);
        Assert.Equal("SENIOR", configDto.LevelThresholds[2].Level);
        Assert.Equal(75, configDto.LevelThresholds[2].MinPercent);

        Assert.Equal(2, configDto.Sections.Count);
        Assert.Equal("csharp-basics", configDto.Sections[0].Key);
        Assert.Equal("Основы C#", configDto.Sections[0].Title);
        Assert.Equal(1.0m, configDto.Sections[0].Weight);
        Assert.Equal(recommendedCourseId, configDto.Sections[0].RecommendedCourseId);
        Assert.Equal("csharp-advanced", configDto.Sections[1].Key);
        Assert.Equal(2.5m, configDto.Sections[1].Weight);
        Assert.Null(configDto.Sections[1].RecommendedCourseId);
        Assert.Equal(fallbackCourseId, configDto.FallbackCourseId);
    }

    [Fact]
    public async Task GetQuizAnswerKey_AsAuthorRole_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: false);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/internal/quizzes/{quizId}/answer-key", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ===== Helpers =====

    private static List<QuizQuestionRequest> BuildValidQuestionRequests() =>
    [
        new QuizQuestionRequest(
            null,
            "SINGLE_CHOICE",
            "Что такое CLR?",
            [new QuizOptionRequest(_singleOptionA, "Среда выполнения"), new QuizOptionRequest(_singleOptionB, "Компилятор")],
            [_singleOptionA]),
        new QuizQuestionRequest(
            null,
            "MULTI_CHOICE",
            "Какие из этих типов — ссылочные?",
            [
                new QuizOptionRequest(_multiOptionA, "string"),
                new QuizOptionRequest(_multiOptionB, "int"),
                new QuizOptionRequest(_multiOptionC, "object"),
            ],
            [_multiOptionA, _multiOptionC]),
        new QuizQuestionRequest(
            null,
            "OPEN_TEXT",
            "Объясните difference между class и struct",
            null,
            null,
            "Эталонное объяснение"),
    ];

    private async Task<Guid> SeedMaterialAsync(Guid authorId, AccessType accessType, string? title = null)
    {
        Guid materialId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId,
                Title.Create(title ?? $"Материал {Guid.NewGuid():N}").Value,
                MaterialKind.ARTICLE,
                accessType);
            material.SetContent(MarkdownContent.Create("# Тело материала").Value);
            UnitResult<Error> publishResult = material.Publish();
            Assert.True(publishResult.IsSuccess);

            db.Materials.Add(material);
            await db.SaveChangesAsync();
            materialId = material.Id;
        });
        return materialId;
    }

    private async Task<Guid> SeedQuizAsync(
        Guid authorId,
        bool publish,
        QuizPurpose purpose = QuizPurpose.MATERIAL_CHECK,
        LevelTestConfig? levelTestConfig = null,
        AccessType accessType = AccessType.PUBLIC)
    {
        Guid quizId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            List<QuizQuestion> questions = BuildDomainQuestions();
            Quiz quiz = Quiz.Create(
                authorId,
                Title.Create($"Квиз {Guid.NewGuid():N}").Value,
                questions,
                purpose: purpose,
                levelTestConfig: levelTestConfig,
                accessType: accessType).Value;

            if (publish)
            {
                UnitResult<Error> publishResult = quiz.Publish();
                Assert.True(publishResult.IsSuccess);
            }

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            quizId = quiz.Id;
        });
        return quizId;
    }

    /// <summary>Привязывает квиз к материалу напрямую в БД (materials.quiz_id, #489).</summary>
    private async Task BindQuizToMaterialInDbAsync(Guid materialId, Guid quizId)
    {
        await ExecuteInDb(async db =>
        {
            Material material = await db.Materials.FirstAsync(m => m.Id == materialId);
            material.AttachQuiz(quizId);
            await db.SaveChangesAsync();
        });
    }

    private static List<QuizQuestion> BuildDomainQuestions() =>
    [
        QuizQuestion.Create(
            Guid.NewGuid(),
            QuizQuestionType.SINGLE_CHOICE,
            "Что такое CLR?",
            [
                QuizOption.Create(_singleOptionA, "Среда выполнения").Value,
                QuizOption.Create(_singleOptionB, "Компилятор").Value,
            ],
            [_singleOptionA],
            referenceAnswer: null,
            section: "csharp-basics",
            difficulty: QuestionDifficulty.JUNIOR).Value,
        QuizQuestion.Create(
            Guid.NewGuid(),
            QuizQuestionType.MULTI_CHOICE,
            "Какие из этих типов — ссылочные?",
            [
                QuizOption.Create(_multiOptionA, "string").Value,
                QuizOption.Create(_multiOptionB, "int").Value,
                QuizOption.Create(_multiOptionC, "object").Value,
            ],
            [_multiOptionA, _multiOptionC],
            referenceAnswer: null).Value,
        QuizQuestion.Create(
            Guid.NewGuid(),
            QuizQuestionType.OPEN_TEXT,
            "Объясните difference между class и struct",
            [],
            [],
            "Эталонный ответ для грейдера").Value,
    ];
}
