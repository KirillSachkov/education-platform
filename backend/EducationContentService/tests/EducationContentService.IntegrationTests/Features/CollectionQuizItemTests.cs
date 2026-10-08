using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Collections;
using EducationContentService.Contracts;
using EducationContentService.Domain;
using EducationContentService.Domain.Collections;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>
///     Tests for #491 — generic-элементы подборок: квиз как item подборки
///     (<c>collection_items.item_type='QUIZ'</c> + <c>reference_id</c>). Покрывает author-flow
///     добавления, detail-проекцию (quizTitle + questionsCount), per-item partial-access
///     по СОБСТВЕННОМУ <c>quizzes.access_type</c> + <c>course_quizzes</c>, скрытие DRAFT-квизов
///     от студентов, list-enricher и каскад DeleteQuiz.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class CollectionQuizItemTests : EducationContentServiceTestsBase
{
    public CollectionQuizItemTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task AddQuizItem_AuthorFlow_DetailReturnsQuizItemWithQuestionsCount()
    {
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);
        Guid sectionId = await AddSectionViaApi(collectionId, ct);
        Guid quizId = await SeedQuizInDb(publish: true, AccessType.PUBLIC, quizTitle: "Проверь себя: CLR");

        HttpResponseMessage itemResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(quizId, "QUIZ"),
            ct);
        Assert.Equal(HttpStatusCode.OK, itemResp.StatusCode);

        await ExecuteInDb(async db =>
        {
            CollectionItem? item = await db.CollectionItems
                .FirstOrDefaultAsync(i => i.SectionId == sectionId, ct);
            Assert.NotNull(item);
            Assert.Equal(CollectionItemType.QUIZ, item.ItemType);
            Assert.Equal(quizId, item.ReferenceId);
        });

        // Publish проходит с единственным QUIZ-элементом (HasAnyForCollectionAsync generic).
        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        HttpResponseMessage detailResp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);
        Assert.Equal(HttpStatusCode.OK, detailResp.StatusCode);

        var detail = await ReadResultAsync<CollectionDetailDto>(detailResp);
        CollectionItemDto quizItem = detail.Sections.SelectMany(s => s.Items)
            .Single(i => i.ReferenceId == quizId);

        Assert.Equal("QUIZ", quizItem.ItemType);
        Assert.Equal("Проверь себя: CLR", quizItem.QuizTitle);
        Assert.Equal(2, quizItem.QuestionsCount);
        Assert.Null(quizItem.Material);
    }

    [Fact]
    public async Task Detail_PublicQuizInsideEnrolledCollection_Anonymous_QuizItemAccessible()
    {
        // Partial-access: header ENROLLED-подборки залочен аноному, но PUBLIC-квиз внутри
        // остаётся доступным (зеркало PUBLIC-материала).
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid collectionId = await CreateCollectionViaApi(ct, courseId); // ENROLLED by default
        Guid sectionId = await AddSectionViaApi(collectionId, ct);
        Guid quizId = await SeedQuizInDb(publish: true, AccessType.PUBLIC);
        await AddQuizItemViaApi(collectionId, sectionId, quizId, ct);

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        RemoveAuthentication();
        HttpResponseMessage resp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var detail = await ReadResultAsync<CollectionDetailDto>(resp);
        Assert.False(detail.IsAccessible);
        Assert.Equal("anonymous", detail.LockReason);

        CollectionItemDto quizItem = detail.Sections.SelectMany(s => s.Items)
            .Single(i => i.ReferenceId == quizId);
        Assert.True(quizItem.IsAccessible, "PUBLIC квиз должен быть доступен анониму.");
        Assert.Null(quizItem.LockReason);
    }

    [Fact]
    public async Task Detail_EnrolledQuiz_AnonymousAndNoGrantUser_QuizItemLocked()
    {
        // ENROLLED-квиз, привязанный к курсу через course_quizzes: аноним → lockReason=anonymous,
        // залогиненный без grant'ов → plan_required (зеркало ENROLLED-материала).
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid collectionId = await CreateCollectionViaApi(ct, courseId);
        Guid sectionId = await AddSectionViaApi(collectionId, ct);
        Guid quizId = await SeedQuizInDb(publish: true, AccessType.ENROLLED, courseId: courseId);
        await AddQuizItemViaApi(collectionId, sectionId, quizId, ct);

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        RemoveAuthentication();
        HttpResponseMessage anonResp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);
        Assert.Equal(HttpStatusCode.OK, anonResp.StatusCode);
        var anonDetail = await ReadResultAsync<CollectionDetailDto>(anonResp);
        CollectionItemDto anonItem = anonDetail.Sections.SelectMany(s => s.Items)
            .Single(i => i.ReferenceId == quizId);
        Assert.False(anonItem.IsAccessible, "ENROLLED квиз должен быть залочен анониму.");
        Assert.Equal("anonymous", anonItem.LockReason);

        AuthenticateAs(Guid.NewGuid(), "platform-student");
        HttpResponseMessage authResp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);
        Assert.Equal(HttpStatusCode.OK, authResp.StatusCode);
        var authDetail = await ReadResultAsync<CollectionDetailDto>(authResp);
        CollectionItemDto authItem = authDetail.Sections.SelectMany(s => s.Items)
            .Single(i => i.ReferenceId == quizId);
        Assert.False(authItem.IsAccessible);
        Assert.Equal("plan_required", authItem.LockReason);
    }

    [Fact]
    public async Task Detail_DraftQuiz_VisibleToAuthor_HiddenFromStudent()
    {
        // Items SQL зеркалит материал: DRAFT-квиз отдаётся ТОЛЬКО его автору (editor-preview),
        // студент его в detail не видит вовсе.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        AuthenticateAs(authorId, "platform-author");

        Guid collectionId = await CreateCollectionViaApi(ct);
        Guid sectionId = await AddSectionViaApi(collectionId, ct);
        Guid draftQuizId = await SeedQuizInDb(publish: false, AccessType.PUBLIC, authorId: authorId);
        await AddQuizItemViaApi(collectionId, sectionId, draftQuizId, ct);

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        // Автор видит свой DRAFT-квиз.
        HttpResponseMessage authorResp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);
        Assert.Equal(HttpStatusCode.OK, authorResp.StatusCode);
        var authorDetail = await ReadResultAsync<CollectionDetailDto>(authorResp);
        Assert.Contains(
            authorDetail.Sections.SelectMany(s => s.Items),
            i => i.ReferenceId == draftQuizId);

        // Студент — нет (item отфильтрован по status в SQL).
        AuthenticateAs(Guid.NewGuid(), "platform-student");
        HttpResponseMessage studentResp = await AppHttpClient.GetAsync(
            $"/collections/{collectionId}/detail", ct);
        Assert.Equal(HttpStatusCode.OK, studentResp.StatusCode);
        var studentDetail = await ReadResultAsync<CollectionDetailDto>(studentResp);
        Assert.DoesNotContain(
            studentDetail.Sections.SelectMany(s => s.Items),
            i => i.ReferenceId == draftQuizId);
    }

    [Fact]
    public async Task AddQuizItem_NonexistentQuiz_Returns404()
    {
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);
        Guid sectionId = await AddSectionViaApi(collectionId, ct);

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(Guid.NewGuid(), "QUIZ"),
            ct);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AddQuizItem_DuplicateInSection_Returns409()
    {
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);
        Guid sectionId = await AddSectionViaApi(collectionId, ct);
        Guid quizId = await SeedQuizInDb(publish: true, AccessType.PUBLIC);

        await AddQuizItemViaApi(collectionId, sectionId, quizId, ct);

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(quizId, "QUIZ"),
            ct);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task AddItem_InvalidItemType_Returns400()
    {
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);
        Guid sectionId = await AddSectionViaApi(collectionId, ct);

        HttpResponseMessage resp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(Guid.NewGuid(), "BANANA"),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteQuiz_CascadesCollectionItems()
    {
        // ST-14 каскад: hard-delete квиза сносит его QUIZ-элементы из подборок
        // (generic-ссылка без FK — иначе orphan-строки).
        CancellationToken ct = CancellationToken.None;
        Guid collectionId = await CreateCollectionViaApi(ct);
        Guid sectionId = await AddSectionViaApi(collectionId, ct);
        Guid quizId = await SeedQuizInDb(publish: true, AccessType.PUBLIC);
        await AddQuizItemViaApi(collectionId, sectionId, quizId, ct);

        HttpResponseMessage deleteResp = await AppHttpClient.DeleteAsync($"/quizzes/{quizId}", ct);
        Assert.Equal(HttpStatusCode.OK, deleteResp.StatusCode);

        await ExecuteInDb(async db =>
        {
            bool anyItemLeft = await db.CollectionItems
                .AnyAsync(i => i.ItemType == CollectionItemType.QUIZ && i.ReferenceId == quizId, ct);
            Assert.False(anyItemLeft, "QUIZ-элементы подборок должны каскадно удаляться с квизом.");
        });
    }

    [Fact]
    public async Task List_EnrolledCollectionWithOnlyPublicQuizItem_Anonymous_CardUnlocked()
    {
        // CollectionAccessEnricher учитывает quiz-items в правиле «хоть один item доступен»:
        // ENROLLED-подборка с единственным PUBLIC-квизом → карточка без замка для анонима.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid collectionId = await CreateCollectionViaApi(ct, courseId);
        Guid sectionId = await AddSectionViaApi(collectionId, ct);
        Guid quizId = await SeedQuizInDb(publish: true, AccessType.PUBLIC);
        await AddQuizItemViaApi(collectionId, sectionId, quizId, ct);

        HttpResponseMessage pubResp = await AppHttpClient.PostAsync(
            $"/collections/{collectionId}/publish", content: null, ct);
        pubResp.EnsureSuccessStatusCode();

        Guid authorId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Collection c = await db.Set<Collection>().FirstAsync(x => x.Id == collectionId, ct);
            authorId = c.AuthorId;
        });

        RemoveAuthentication();
        HttpResponseMessage listResp = await AppHttpClient.GetAsync(
            $"/authors/{authorId}/collections?limit=20", ct);
        listResp.EnsureSuccessStatusCode();

        var body = await ReadResultAsync<CursorResponse<CollectionSummaryDto>>(listResp);
        CollectionSummaryDto? target = body.Items.FirstOrDefault(i => i.Id == collectionId);
        Assert.NotNull(target);
        Assert.True(target.IsAccessible, "Card должна быть unlocked: внутри есть PUBLIC квиз.");
        Assert.Null(target.LockReason);
    }

    // ===== Helpers =====

    private async Task<Guid> CreateCollectionViaApi(CancellationToken ct, Guid? courseId = null)
    {
        HttpResponseMessage createResp = await AppHttpClient.PostAsJsonAsync(
            "/collections",
            new CreateCollectionRequest("Quiz collection", null, courseId),
            ct);
        createResp.EnsureSuccessStatusCode();
        return await ReadResultAsync<Guid>(createResp);
    }

    private async Task<Guid> AddSectionViaApi(Guid collectionId, CancellationToken ct)
    {
        HttpResponseMessage secResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections",
            new AddSectionRequest(null, null),
            ct);
        secResp.EnsureSuccessStatusCode();
        return await ReadResultAsync<Guid>(secResp);
    }

    private async Task AddQuizItemViaApi(
        Guid collectionId, Guid sectionId, Guid quizId, CancellationToken ct)
    {
        HttpResponseMessage itemResp = await AppHttpClient.PostAsJsonAsync(
            $"/collections/{collectionId}/sections/{sectionId}/items",
            new AddItemRequest(quizId, "QUIZ"),
            ct);
        itemResp.EnsureSuccessStatusCode();
    }

    /// <summary>
    ///     Сидит квиз (2 вопроса) напрямую в БД. <paramref name="courseId"/> добавляет
    ///     привязку <c>course_quizzes</c> — источник courseIds для ENROLLED-гейта.
    /// </summary>
    private async Task<Guid> SeedQuizInDb(
        bool publish,
        AccessType accessType,
        Guid? courseId = null,
        Guid? authorId = null,
        string? quizTitle = null)
    {
        Guid quizId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Guid optionA = Guid.NewGuid();
            Guid optionB = Guid.NewGuid();
            List<QuizQuestion> questions =
            [
                QuizQuestion.Create(
                    Guid.NewGuid(),
                    QuizQuestionType.SINGLE_CHOICE,
                    "Что такое CLR?",
                    [
                        QuizOption.Create(optionA, "Среда выполнения").Value,
                        QuizOption.Create(optionB, "Компилятор").Value,
                    ],
                    [optionA],
                    referenceAnswer: null).Value,
                QuizQuestion.Create(
                    Guid.NewGuid(),
                    QuizQuestionType.OPEN_TEXT,
                    "Объясните разницу между class и struct",
                    [],
                    [],
                    "Эталонное объяснение").Value,
            ];

            Quiz quiz = Quiz.Create(
                authorId ?? Guid.NewGuid(),
                Title.Create(quizTitle ?? $"Квиз {Guid.NewGuid():N}").Value,
                questions,
                accessType: accessType).Value;

            if (publish)
            {
                UnitResult<Error> publishResult = quiz.Publish();
                Assert.True(publishResult.IsSuccess);
            }

            db.Quizzes.Add(quiz);

            if (courseId is not null)
                db.CourseQuizzes.Add(new CourseQuiz(courseId.Value, quiz.Id, SortKey.Initial()));

            await db.SaveChangesAsync();
            quizId = quiz.Id;
        });
        return quizId;
    }

    private async Task<Guid> CreateCourseInDb(CancellationToken ct)
    {
        Guid courseId = Guid.NewGuid();
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId: Guid.NewGuid(),
                title: Title.Create("QC").Value,
                description: Description.Create("D").Value,
                slug: CourseSlug.Create("qc-" + Guid.NewGuid().ToString("N")[..8]).Value,
                SortKey.Initial());
            db.Set<Course>().Add(course);
            await db.SaveChangesAsync(ct);
            courseId = course.Id;
        });
        return courseId;
    }
}
