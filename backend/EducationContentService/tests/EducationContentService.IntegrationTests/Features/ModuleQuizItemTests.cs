using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.Modules;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>
///     ST-12 (#492): квиз как элемент модуля. Attach (<c>POST /modules/{id}/quizzes</c>,
///     INV-4-зеркало — auto-create <c>course_quizzes</c> + <c>quiz.access_changed</c>),
///     detach последнего item'а (derived-привязка снимается, теги сужаются),
///     transfer между курсами (привязки переезжают), generic move/reorder,
///     PUBLISHED-видимость в curriculum, DRAFT-видимость в builder,
///     module_items каскад DeleteQuiz.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class ModuleQuizItemTests : EducationContentServiceTestsBase
{
    private static readonly Guid _optionA = Guid.NewGuid();
    private static readonly Guid _optionB = Guid.NewGuid();

    private readonly IntegrationTestsWebFactory _factory;

    public ModuleQuizItemTests(IntegrationTestsWebFactory factory) : base(factory)
    {
        _factory = factory;
    }

    // ===== Attach =====

    [Fact]
    public async Task AttachQuizToModule_CreatesModuleItem_AutoCreatesCourseQuiz_AndPublishesAccessChanged()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (Guid courseId, Guid moduleId) = await SeedCourseWithModuleAsync(authorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{moduleId}/quizzes", new { QuizId = quizId }, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            ModuleItem? item = await db.ModuleItems.FirstOrDefaultAsync(
                mi => mi.ModuleId == moduleId && mi.ReferenceId == quizId, ct);
            Assert.NotNull(item);
            Assert.Equal(ModuleItemType.Quiz, item.ItemType);

            CourseQuiz? binding = await db.CourseQuizzes.FirstOrDefaultAsync(
                cq => cq.CourseId == courseId && cq.QuizId == quizId, ct);
            Assert.NotNull(binding);
        });

        QuizAccessChanged ev = Assert.Single(_factory.OutboxCollector.OfType<QuizAccessChanged>());
        Assert.Equal(quizId, ev.QuizId);
        Assert.Equal("ENROLLED", ev.AccessType);
        Assert.Equal(authorId, ev.AuthorId);
        Assert.Equal([courseId], ev.CourseIds);
    }

    [Fact]
    public async Task AttachQuizToModule_SecondModuleSameCourse_DoesNotDuplicateCourseQuiz()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (Guid courseId, Guid moduleAId) = await SeedCourseWithModuleAsync(authorId);
        Guid moduleBId = await AddModuleToCourseAsync(courseId, authorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleAId}/quizzes", new { QuizId = quizId }, ct))
            .EnsureSuccessStatusCode();
        _factory.OutboxCollector.Clear();

        HttpResponseMessage second = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{moduleBId}/quizzes", new { QuizId = quizId }, ct);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await ExecuteInDb(async db =>
        {
            int bindings = await db.CourseQuizzes.CountAsync(
                cq => cq.CourseId == courseId && cq.QuizId == quizId, ct);
            Assert.Equal(1, bindings);

            int items = await db.ModuleItems.CountAsync(
                mi => mi.ReferenceId == quizId && mi.ItemType == ModuleItemType.Quiz, ct);
            Assert.Equal(2, items);
        });

        // Привязка уже существовала — повторного quiz.access_changed быть не должно.
        Assert.Empty(_factory.OutboxCollector.OfType<QuizAccessChanged>());
    }

    [Fact]
    public async Task AttachQuizToModule_DraftQuiz_IsAllowed()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (_, Guid moduleId) = await SeedCourseWithModuleAsync(authorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: false);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{moduleId}/quizzes", new { QuizId = quizId }, ct);

        // DRAFT-квиз привязывать можно (зеркало DRAFT-материала) — студенты его не видят.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AttachQuizToModule_ForeignQuiz_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid otherAuthorId = Guid.NewGuid();
        (_, Guid moduleId) = await SeedCourseWithModuleAsync(authorId);
        Guid foreignQuizId = await SeedQuizAsync(otherAuthorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{moduleId}/quizzes", new { QuizId = foreignQuizId }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AttachQuizToModule_ForeignCourse_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid otherAuthorId = Guid.NewGuid();
        (_, Guid foreignModuleId) = await SeedCourseWithModuleAsync(otherAuthorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{foreignModuleId}/quizzes", new { QuizId = quizId }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AttachQuizToModule_OrphanModule_Returns400()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid moduleId = await SeedOrphanModuleAsync(authorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/modules/{moduleId}/quizzes", new { QuizId = quizId }, ct);

        // INV-4-зеркало требует курс — orphan-модуль отклоняется как у материала.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ===== Detach =====

    [Fact]
    public async Task DetachQuizItem_LastInCourse_RemovesCourseQuizAndPublishesAccessChanged()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (Guid courseId, Guid moduleId) = await SeedCourseWithModuleAsync(authorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);

        AuthenticateAs(authorId, "platform-author");
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleId}/quizzes", new { QuizId = quizId }, ct))
            .EnsureSuccessStatusCode();
        _factory.OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/modules/{moduleId}/items/{quizId}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            bool itemExists = await db.ModuleItems.AnyAsync(
                mi => mi.ModuleId == moduleId && mi.ReferenceId == quizId, ct);
            Assert.False(itemExists);

            bool bindingExists = await db.CourseQuizzes.AnyAsync(
                cq => cq.CourseId == courseId && cq.QuizId == quizId, ct);
            Assert.False(bindingExists, "Последний quiz-item открепили — course_quizzes должна уйти.");
        });

        QuizAccessChanged ev = Assert.Single(_factory.OutboxCollector.OfType<QuizAccessChanged>());
        Assert.Equal(quizId, ev.QuizId);
        Assert.Equal("ENROLLED", ev.AccessType);
        Assert.Empty(ev.CourseIds);
    }

    [Fact]
    public async Task DetachQuizItem_QuizStillInAnotherModuleOfCourse_KeepsCourseQuiz()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (Guid courseId, Guid moduleAId) = await SeedCourseWithModuleAsync(authorId);
        Guid moduleBId = await AddModuleToCourseAsync(courseId, authorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleAId}/quizzes", new { QuizId = quizId }, ct))
            .EnsureSuccessStatusCode();
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleBId}/quizzes", new { QuizId = quizId }, ct))
            .EnsureSuccessStatusCode();
        _factory.OutboxCollector.Clear();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/modules/{moduleAId}/items/{quizId}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            bool bindingExists = await db.CourseQuizzes.AnyAsync(
                cq => cq.CourseId == courseId && cq.QuizId == quizId, ct);
            Assert.True(bindingExists, "Квиз ещё размещён в другом модуле курса — привязка живёт.");
        });

        Assert.Empty(_factory.OutboxCollector.OfType<QuizAccessChanged>());
    }

    // ===== Transfer / Move =====

    [Fact]
    public async Task TransferQuizItem_BetweenCourses_MovesCourseQuizBinding()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (Guid courseAId, Guid moduleAId) = await SeedCourseWithModuleAsync(authorId);
        (Guid courseBId, Guid moduleBId) = await SeedCourseWithModuleAsync(authorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: true, accessType: AccessType.ENROLLED);

        AuthenticateAs(authorId, "platform-author");
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleAId}/quizzes", new { QuizId = quizId }, ct))
            .EnsureSuccessStatusCode();
        _factory.OutboxCollector.Clear();

        HttpResponseMessage response = await PatchAsJsonAsync(
            $"/modules/{moduleAId}/items/{quizId}/transfer",
            new TransferModuleItemRequest(moduleBId, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            ModuleItem item = await db.ModuleItems.SingleAsync(
                mi => mi.ReferenceId == quizId && mi.ItemType == ModuleItemType.Quiz, ct);
            Assert.Equal(moduleBId, item.ModuleId);

            bool sourceBinding = await db.CourseQuizzes.AnyAsync(
                cq => cq.CourseId == courseAId && cq.QuizId == quizId, ct);
            Assert.False(sourceBinding, "Source-курс потерял последний quiz-item — привязка снята.");

            bool targetBinding = await db.CourseQuizzes.AnyAsync(
                cq => cq.CourseId == courseBId && cq.QuizId == quizId, ct);
            Assert.True(targetBinding, "Target-курс получил derived-привязку (INV-4-зеркало).");
        });

        QuizAccessChanged ev = Assert.Single(_factory.OutboxCollector.OfType<QuizAccessChanged>());
        Assert.Equal(quizId, ev.QuizId);
        Assert.Equal([courseBId], ev.CourseIds);
    }

    [Fact]
    public async Task TransferQuizItem_WithinSameCourse_KeepsBindingAndPublishesNothing()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (Guid courseId, Guid moduleAId) = await SeedCourseWithModuleAsync(authorId);
        Guid moduleBId = await AddModuleToCourseAsync(courseId, authorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleAId}/quizzes", new { QuizId = quizId }, ct))
            .EnsureSuccessStatusCode();
        _factory.OutboxCollector.Clear();

        HttpResponseMessage response = await PatchAsJsonAsync(
            $"/modules/{moduleAId}/items/{quizId}/transfer",
            new TransferModuleItemRequest(moduleBId, null, null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            bool bindingExists = await db.CourseQuizzes.AnyAsync(
                cq => cq.CourseId == courseId && cq.QuizId == quizId, ct);
            Assert.True(bindingExists);
        });

        Assert.Empty(_factory.OutboxCollector.OfType<QuizAccessChanged>());
    }

    [Fact]
    public async Task MoveQuizItem_WithinModule_ReordersBySortKey()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (_, Guid moduleId) = await SeedCourseWithModuleAsync(authorId);
        Guid quizAId = await SeedQuizAsync(authorId, publish: true);
        Guid quizBId = await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleId}/quizzes", new { QuizId = quizAId }, ct))
            .EnsureSuccessStatusCode();
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleId}/quizzes", new { QuizId = quizBId }, ct))
            .EnsureSuccessStatusCode();

        string lastSortKey = await ExecuteInDb(async db =>
        {
            ModuleItem itemB = await db.ModuleItems.SingleAsync(
                mi => mi.ModuleId == moduleId && mi.ReferenceId == quizBId, ct);
            return itemB.SortKey.Value;
        });

        HttpResponseMessage response = await PatchAsJsonAsync(
            $"/modules/{moduleId}/items/{quizAId}/move",
            new MoveModuleItemRequest(AfterSortKey: lastSortKey, BeforeSortKey: null));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            ModuleItem itemA = await db.ModuleItems.SingleAsync(
                mi => mi.ModuleId == moduleId && mi.ReferenceId == quizAId, ct);
            ModuleItem itemB = await db.ModuleItems.SingleAsync(
                mi => mi.ModuleId == moduleId && mi.ReferenceId == quizBId, ct);
            Assert.True(
                string.CompareOrdinal(itemA.SortKey.Value, itemB.SortKey.Value) > 0,
                "После move quizA должен стоять после quizB.");
        });
    }

    // ===== Curriculum / Builder =====

    [Fact]
    public async Task Curriculum_PublishedQuizVisibleWithQuestionsCountAndQuizId_DraftQuizHidden()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (Guid courseId, Guid moduleId) = await SeedCourseWithModuleAsync(authorId);
        Guid publishedQuizId = await SeedQuizAsync(
            authorId, publish: true, accessType: AccessType.REGISTERED, title: "Опубликованный квиз");
        Guid draftQuizId = await SeedQuizAsync(authorId, publish: false, title: "Черновик квиза");

        AuthenticateAs(authorId, "platform-author");
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleId}/quizzes", new { QuizId = publishedQuizId }, ct))
            .EnsureSuccessStatusCode();
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleId}/quizzes", new { QuizId = draftQuizId }, ct))
            .EnsureSuccessStatusCode();

        RemoveAuthentication();
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/curriculum", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseCurriculumDto curriculum = await ReadResultAsync<CourseCurriculumDto>(response);
        List<CurriculumItemDto> items = curriculum.Sections
            .Where(s => s.Id == moduleId)
            .SelectMany(s => s.Items)
            .ToList();

        CurriculumItemDto quizItem = Assert.Single(items, i => i.Id == publishedQuizId);
        Assert.Equal("Quiz", quizItem.ItemType);
        Assert.Equal("Опубликованный квиз", quizItem.Title);
        Assert.Equal("REGISTERED", quizItem.AccessType);
        Assert.Equal(2, quizItem.QuestionsCount);
        Assert.Equal(publishedQuizId, quizItem.QuizId);

        Assert.DoesNotContain(items, i => i.Id == draftQuizId);
    }

    [Fact]
    public async Task Builder_DraftQuizItem_VisibleToAuthorWithStatusAndQuizId()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (Guid courseId, Guid moduleId) = await SeedCourseWithModuleAsync(authorId);
        Guid draftQuizId = await SeedQuizAsync(authorId, publish: false, title: "Черновик для билдера");

        AuthenticateAs(authorId, "platform-author");
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleId}/quizzes", new { QuizId = draftQuizId }, ct))
            .EnsureSuccessStatusCode();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/builder", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseBuilderDto builder = await ReadResultAsync<CourseBuilderDto>(response);
        ModuleItemDto quizItem = builder.Sections
            .Where(s => s.Id == moduleId)
            .SelectMany(s => s.Items)
            .Single(i => i.ReferenceId == draftQuizId);

        Assert.Equal("Quiz", quizItem.ItemType);
        Assert.Equal("Черновик для билдера", quizItem.Title);
        Assert.Equal("DRAFT", quizItem.Status);
        Assert.Equal(2, quizItem.QuestionsCount);
        Assert.Equal(draftQuizId, quizItem.QuizId);
    }

    // ===== DeleteQuiz cascade =====

    [Fact]
    public async Task DeleteQuiz_CascadesModuleItems()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        (Guid courseId, Guid moduleId) = await SeedCourseWithModuleAsync(authorId);
        Guid quizId = await SeedQuizAsync(authorId, publish: true);

        AuthenticateAs(authorId, "platform-author");
        (await AppHttpClient.PostAsJsonAsync($"/modules/{moduleId}/quizzes", new { QuizId = quizId }, ct))
            .EnsureSuccessStatusCode();

        HttpResponseMessage response = await AppHttpClient.DeleteAsync($"/quizzes/{quizId}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await ExecuteInDb(async db =>
        {
            bool itemExists = await db.ModuleItems.AnyAsync(mi => mi.ReferenceId == quizId, ct);
            Assert.False(itemExists, "DeleteQuiz должен каскадно снести quiz-элементы модулей.");

            bool bindingExists = await db.CourseQuizzes.AnyAsync(cq => cq.QuizId == quizId, ct);
            Assert.False(bindingExists);
        });
    }

    // ===== Helpers =====

    private async Task<(Guid CourseId, Guid ModuleId)> SeedCourseWithModuleAsync(Guid authorId)
    {
        Guid courseId = Guid.Empty;
        Guid moduleId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {Guid.NewGuid():N}").Value,
                Description.Create("Описание").Value,
                CourseSlug.Create($"course-{Guid.CreateVersion7():N}").Value,
                SortKey.Initial());
            course.Publish();
            db.Courses.Add(course);
            courseId = course.Id;

            var module = new Module(
                authorId,
                Title.Create($"Модуль {Guid.NewGuid():N}").Value,
                Description.Create("Описание модуля").Value);
            module.Publish();
            db.Modules.Add(module);
            moduleId = module.Id;

            db.CourseItems.Add(new CourseItem(
                courseId, CourseItemType.Module, moduleId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync();
        });
        return (courseId, moduleId);
    }

    private async Task<Guid> AddModuleToCourseAsync(Guid courseId, Guid authorId)
    {
        Guid moduleId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var module = new Module(
                authorId,
                Title.Create($"Модуль {Guid.NewGuid():N}").Value,
                Description.Create("Описание модуля").Value);
            module.Publish();
            db.Modules.Add(module);
            moduleId = module.Id;

            string? lastSortKey = await db.CourseItems
                .Where(ci => ci.CourseId == courseId)
                .OrderByDescending(ci => ci.SortKey)
                .Select(ci => ci.SortKey.Value)
                .FirstOrDefaultAsync();

            SortKey nextKey = lastSortKey is null
                ? SortKey.Initial()
                : SortKey.After(SortKey.Create(lastSortKey).Value);

            db.CourseItems.Add(new CourseItem(
                courseId, CourseItemType.Module, moduleId, nextKey, isOptional: false));
            await db.SaveChangesAsync();
        });
        return moduleId;
    }

    private async Task<Guid> SeedOrphanModuleAsync(Guid authorId)
    {
        Guid moduleId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var module = new Module(
                authorId,
                Title.Create($"Orphan модуль {Guid.NewGuid():N}").Value,
                Description.Create("Без курса").Value);
            db.Modules.Add(module);
            moduleId = module.Id;
            await db.SaveChangesAsync();
        });
        return moduleId;
    }

    private async Task<Guid> SeedQuizAsync(
        Guid authorId, bool publish, AccessType accessType = AccessType.PUBLIC, string? title = null)
    {
        Guid quizId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Quiz quiz = Quiz.Create(
                authorId,
                Title.Create(title ?? $"Квиз {Guid.NewGuid():N}").Value,
                BuildDomainQuestions(),
                accessType: accessType).Value;

            if (publish)
                quiz.Publish();

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            quizId = quiz.Id;
        });
        return quizId;
    }

    private static List<QuizQuestion> BuildDomainQuestions() =>
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
