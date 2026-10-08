using System.Net;
using System.Net.Http.Json;
using ContentAccess;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Ordering;
using Shared.Messaging.IntegrationEvents.Education.Events;
using SharedKernel;
using StackExchange.Redis;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>
///     ST-11 (#490): события доступа квиза (quiz.published / quiz.access_changed /
///     quiz.hard_deleted), self-consume sync-handlers (Redis-теги resource type
///     <c>quiz</c>) и студенческий read-гейт <c>GET /quizzes/{id}/student</c> +
///     второй слой quiz-entitlement в <c>GET /materials/{id}/quiz</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class QuizAccessSyncTests : EducationContentServiceTestsBase
{
    private static readonly Guid _singleOptionA = Guid.NewGuid();
    private static readonly Guid _singleOptionB = Guid.NewGuid();

    private readonly IntegrationTestsWebFactory _factory;

    public QuizAccessSyncTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    private static RedisKey QuizAccessKey(Guid quizId) => $"resource-access:quiz:{quizId:D}";

    // ===== L2: endpoint → outbox publish =====

    [Fact]
    public async Task PublishQuiz_PublishesQuizPublishedEvent_WithAccessTypeAndCourseIds()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: false, accessType: AccessType.ENROLLED);
        Guid courseId = await SeedCourseWithQuizBindingAsync(authorId, quizId);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsync($"/quizzes/{quizId}/publish", null, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        QuizPublished published = Assert.Single(_factory.OutboxCollector.OfType<QuizPublished>());
        Assert.Equal(quizId, published.QuizId);
        Assert.Equal(authorId, published.AuthorId);
        Assert.Equal("ENROLLED", published.AccessType);
        Assert.Equal([courseId], published.CourseIds);
    }

    [Fact]
    public async Task UpdateQuiz_AccessTypeChanged_PublishesQuizAccessChanged()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.PUBLIC);
        Guid courseId = await SeedCourseWithQuizBindingAsync(authorId, quizId);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PutAsJsonAsync(
            $"/quizzes/{quizId}",
            new UpdateQuizRequest("Квиз с новым доступом", BuildQuestionRequests(), AccessType: "ENROLLED"),
            ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        QuizAccessChanged ev = Assert.Single(_factory.OutboxCollector.OfType<QuizAccessChanged>());
        Assert.Equal(quizId, ev.QuizId);
        Assert.Equal("ENROLLED", ev.AccessType);
        Assert.Equal(authorId, ev.AuthorId);
        Assert.Equal([courseId], ev.CourseIds);
    }

    [Fact]
    public async Task UpdateQuiz_AccessTypeUnchanged_DoesNotPublishQuizAccessChanged()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.REGISTERED);

        AuthenticateAs(authorId, "platform-author");

        // AccessType=null ⇒ «не менять» — события быть не должно.
        HttpResponseMessage nullResponse = await AppHttpClient.PutAsJsonAsync(
            $"/quizzes/{quizId}",
            new UpdateQuizRequest("Квиз без смены доступа", BuildQuestionRequests()),
            ct);
        Assert.Equal(HttpStatusCode.OK, nullResponse.StatusCode);

        // Явный AccessType, совпадающий с текущим — события тоже нет.
        HttpResponseMessage sameResponse = await AppHttpClient.PutAsJsonAsync(
            $"/quizzes/{quizId}",
            new UpdateQuizRequest("Квиз без смены доступа", BuildQuestionRequests(), AccessType: "REGISTERED"),
            ct);
        Assert.Equal(HttpStatusCode.OK, sameResponse.StatusCode);

        Assert.Empty(_factory.OutboxCollector.OfType<QuizAccessChanged>());
    }

    [Fact]
    public async Task DeleteQuiz_PublishesQuizHardDeleted()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/quizzes/{quizId}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        QuizHardDeleted ev = Assert.Single(_factory.OutboxCollector.OfType<QuizHardDeleted>());
        Assert.Equal(quizId, ev.QuizId);
    }

    // ===== L1: sync-handlers → Redis (mock IConnectionMultiplexer) =====

    [Fact]
    public async Task QuizPublishedHandler_EnrolledCourseBoundQuiz_WritesPlanTags()
    {
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);
        Guid courseId = await SeedCourseWithQuizBindingAsync(authorId, quizId);

        await InvokeMessageAndWaitAsync(new QuizPublished(quizId, authorId, "ENROLLED", [courseId]));

        await _factory.RedisDatabase.Received(1).KeyDeleteAsync(QuizAccessKey(quizId), Arg.Any<CommandFlags>());
        await _factory.RedisDatabase.Received(1).SetAddAsync(
            QuizAccessKey(quizId),
            Arg.Is<RedisValue[]>(values =>
                values.Length == 2 &&
                values.Any(v => v.ToString() == GrantTags.PlanAll()) &&
                values.Any(v => v.ToString() == $"plan:course:{courseId:D}") &&
                values.All(v => !v.ToString().StartsWith(GrantTags.PLAN_LIFETIME_PREFIX, StringComparison.Ordinal))),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task QuizPublishedHandler_PublicOrphanQuiz_WritesPublicTag()
    {
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.PUBLIC);

        await InvokeMessageAndWaitAsync(new QuizPublished(quizId, authorId, "PUBLIC", []));

        await _factory.RedisDatabase.Received(1).SetAddAsync(
            QuizAccessKey(quizId),
            Arg.Is<RedisValue[]>(values =>
                values.Length == 1 && values[0].ToString() == GrantTags.PUBLIC),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task QuizAccessChangedHandler_PublishedQuiz_RewritesTags()
    {
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.REGISTERED);

        await InvokeMessageAndWaitAsync(new QuizAccessChanged(quizId, "REGISTERED", [], authorId));

        await _factory.RedisDatabase.Received(1).KeyDeleteAsync(QuizAccessKey(quizId), Arg.Any<CommandFlags>());
        await _factory.RedisDatabase.Received(1).SetAddAsync(
            QuizAccessKey(quizId),
            Arg.Is<RedisValue[]>(values =>
                values.Length == 1 && values[0].ToString() == GrantTags.AUTHENTICATED),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task QuizAccessChangedHandler_DraftQuiz_SkipsRedisWrite()
    {
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: false, accessType: AccessType.ENROLLED);

        await InvokeMessageAndWaitAsync(new QuizAccessChanged(quizId, "ENROLLED", [], authorId));

        // PUBLISHED-гейт: у DRAFT-квиза тегов нет и появиться они не должны.
        await _factory.RedisDatabase.DidNotReceive()
            .KeyDeleteAsync(QuizAccessKey(quizId), Arg.Any<CommandFlags>());
        await _factory.RedisDatabase.DidNotReceive()
            .SetAddAsync(QuizAccessKey(quizId), Arg.Any<RedisValue[]>(), Arg.Any<CommandFlags>());
    }

    [Fact]
    public async Task QuizHardDeletedHandler_ClearsAccessKey()
    {
        Guid quizId = Guid.NewGuid();

        await InvokeMessageAndWaitAsync(new QuizHardDeleted(quizId));

        await _factory.RedisDatabase.Received(1).KeyDeleteAsync(QuizAccessKey(quizId), Arg.Any<CommandFlags>());
    }

    // ===== GET /quizzes/{id}/student — студенческий read-гейт =====

    [Fact]
    public async Task GetStudentQuiz_PublicQuiz_Anonymous_Returns200_AndNeverLeaksAnswers()
    {
        CancellationToken ct = CancellationToken.None;
        Guid quizId = await SeedQuizAsync(Guid.NewGuid(), publish: true, accessType: AccessType.PUBLIC);

        // DenyAll доказывает, что PUBLIC short-circuit идёт по данным БД и не зовёт
        // entitlement checker — зеркало GetMaterialDetail.
        EntitlementChecker.DenyAll();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/{quizId}/student", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // String-level контракт: ответы не должны присутствовать в JSON ни под каким ключом.
        string rawJson = await response.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain("correctOptionIds", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("referenceAnswer", rawJson, StringComparison.OrdinalIgnoreCase);

        QuizStudentDto studentView = await ReadResultAsync<QuizStudentDto>(response);
        Assert.Equal(quizId, studentView.Id);
        // Standalone-чтение — без материала-контекста.
        Assert.Null(studentView.MaterialId);
        Assert.Equal(2, studentView.Questions.Count);
    }

    [Fact]
    public async Task GetStudentQuiz_EnrolledQuiz_Anonymous_Returns401()
    {
        CancellationToken ct = CancellationToken.None;
        Guid quizId = await SeedQuizAsync(Guid.NewGuid(), publish: true, accessType: AccessType.ENROLLED);

        EntitlementChecker.DenyAll();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/{quizId}/student", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStudentQuiz_EnrolledQuiz_AuthenticatedDenied_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid quizId = await SeedQuizAsync(Guid.NewGuid(), publish: true, accessType: AccessType.ENROLLED);

        EntitlementChecker.DenyAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/{quizId}/student", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetStudentQuiz_EnrolledQuiz_AuthenticatedGranted_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid quizId = await SeedQuizAsync(Guid.NewGuid(), publish: true, accessType: AccessType.ENROLLED);

        EntitlementChecker.GrantAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/{quizId}/student", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizStudentDto studentView = await ReadResultAsync<QuizStudentDto>(response);
        Assert.Equal(quizId, studentView.Id);
    }

    [Fact]
    public async Task GetStudentQuiz_EnrolledQuiz_AsOwner_DeniedEntitlements_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);

        // Владелец проходит по ownership short-circuit даже при DenyAll.
        EntitlementChecker.DenyAll();
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/{quizId}/student", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetStudentQuiz_DraftQuiz_AsOwner_Returns404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid quizId = await SeedQuizAsync(authorId, publish: false, accessType: AccessType.PUBLIC);

        // /student отдаёт только PUBLISHED — DRAFT 404 даже владельцу
        // (у владельца авторский GET /quizzes/{id}).
        EntitlementChecker.GrantAll();
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/quizzes/{quizId}/student", ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ===== GET /materials/{id}/quiz — второй слой: гейт по КВИЗУ (строже из двух) =====

    [Fact]
    public async Task GetMaterialQuiz_EnrolledQuizOnPublicMaterial_Anonymous_Returns401()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        EntitlementChecker.DenyAll();
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        // Материал PUBLIC пропустил бы, но квиз ENROLLED — действует строжайший гейт.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialQuiz_EnrolledQuizOnPublicMaterial_AuthenticatedDenied_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        EntitlementChecker.DenyAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMaterialQuiz_EnrolledQuizOnPublicMaterial_AuthenticatedGranted_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        EntitlementChecker.GrantAll();
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        QuizStudentDto studentView = await ReadResultAsync<QuizStudentDto>(response);
        Assert.Equal(quizId, studentView.Id);
        Assert.Equal(materialId, studentView.MaterialId);
    }

    [Fact]
    public async Task GetMaterialQuiz_EnrolledQuizOnPublicMaterial_AsQuizAuthor_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid materialId = await SeedMaterialAsync(authorId, AccessType.PUBLIC);
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);
        await BindQuizToMaterialInDbAsync(materialId, quizId);

        // Автор квиза проходит quiz-слой по ownership short-circuit даже при DenyAll.
        EntitlementChecker.DenyAll();
        AuthenticateAs(authorId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/materials/{materialId}/quiz", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ===== Helpers =====

    private static List<QuizQuestionRequest> BuildQuestionRequests() =>
    [
        new QuizQuestionRequest(
            null,
            "SINGLE_CHOICE",
            "Что такое CLR?",
            [new QuizOptionRequest(_singleOptionA, "Среда выполнения"), new QuizOptionRequest(_singleOptionB, "Компилятор")],
            [_singleOptionA]),
    ];

    private async Task<Guid> SeedMaterialAsync(Guid authorId, AccessType accessType)
    {
        Guid materialId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId,
                Title.Create($"Материал {Guid.NewGuid():N}").Value,
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

    private async Task<Guid> SeedQuizAsync(Guid authorId, bool publish, AccessType accessType = AccessType.PUBLIC)
    {
        Guid quizId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Quiz quiz = Quiz.Create(
                authorId,
                Title.Create($"Квиз {Guid.NewGuid():N}").Value,
                BuildDomainQuestions(),
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

    /// <summary>Создаёт курс автора и привязку квиза (<c>course_quizzes</c>, #489).</summary>
    private async Task<Guid> SeedCourseWithQuizBindingAsync(Guid authorId, Guid quizId)
    {
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
            await db.SaveChangesAsync();
        });
        return courseId;
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
