using System.Net;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.ProgressLookup;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>
///     S2S quiz module-lookup (ST-13 #493): <c>GET /internal/quizzes/{id}/module-lookup</c> —
///     зеркало material course-contexts для cascade'а passed-попытки квиза в ProgressService.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class QuizModuleLookupTests : EducationContentServiceTestsBase
{
    public QuizModuleLookupTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task QuizModuleLookup_QuizInModule_ReturnsCourseModuleAndItemsTotal()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid moduleId = await CreateModuleAttachedToCourseInDb(courseId, ct);
        Guid quizId = await CreateQuizInDb(ct);
        await AttachItemToModuleInDb(moduleId, ModuleItemType.Quiz, quizId, ct);
        // Второй item в модуле — для проверки ModuleItemsTotal.
        await AttachItemToModuleInDb(moduleId, ModuleItemType.Material, Guid.NewGuid(), ct);

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/quizzes/{quizId}/module-lookup", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<QuizModuleContextDto> contexts =
            await ReadResultAsync<IReadOnlyList<QuizModuleContextDto>>(response);

        QuizModuleContextDto context = Assert.Single(contexts);
        Assert.Equal(courseId, context.CourseId);
        Assert.Equal(moduleId, context.ModuleId);
        Assert.Equal(2, context.ModuleItemsTotal);
    }

    [Fact]
    public async Task QuizModuleLookup_QuizNotInAnyModule_ReturnsEmpty()
    {
        CancellationToken ct = CancellationToken.None;
        Guid quizId = await CreateQuizInDb(ct);

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/quizzes/{quizId}/module-lookup", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<QuizModuleContextDto> contexts =
            await ReadResultAsync<IReadOnlyList<QuizModuleContextDto>>(response);

        Assert.Empty(contexts);
    }

    [Fact]
    public async Task QuizModuleLookup_AsAuthorRole_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/internal/quizzes/{Guid.NewGuid()}/module-lookup");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreateCourseInDb(CancellationToken ct)
    {
        Guid courseId = Guid.NewGuid();
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId: Guid.NewGuid(),
                title: Title.Create("C").Value,
                description: Description.Create("D").Value,
                slug: CourseSlug.Create("c-" + Guid.NewGuid().ToString("N")[..8]).Value,
                SortKey.Initial());
            db.Set<Course>().Add(course);
            await db.SaveChangesAsync(ct);
            courseId = course.Id;
        });
        return courseId;
    }

    private async Task<Guid> CreateModuleAttachedToCourseInDb(Guid courseId, CancellationToken ct)
    {
        Guid moduleId = Guid.NewGuid();
        await ExecuteInDb(async db =>
        {
            var module = new Module(authorId: Guid.NewGuid(), title: Title.Create("M").Value);
            db.Set<Module>().Add(module);
            db.Set<CourseItem>().Add(new CourseItem(
                courseId, CourseItemType.Module, module.Id, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
            moduleId = module.Id;
        });
        return moduleId;
    }

    private async Task<Guid> CreateQuizInDb(CancellationToken ct)
    {
        Guid quizId = Guid.NewGuid();
        await ExecuteInDb(async db =>
        {
            Guid optionA = Guid.NewGuid();
            QuizQuestion question = QuizQuestion.Create(
                Guid.NewGuid(),
                QuizQuestionType.SINGLE_CHOICE,
                "Вопрос квиза",
                [QuizOption.Create(optionA, "Вариант A").Value, QuizOption.Create(Guid.NewGuid(), "Вариант B").Value],
                [optionA],
                referenceAnswer: null).Value;

            Quiz quiz = Quiz.Create(Guid.NewGuid(), Title.Create("Quiz").Value, [question]).Value;
            UnitResult<Error> publish = quiz.Publish();
            Assert.True(publish.IsSuccess, publish.IsFailure ? publish.Error.GetMessage() : null);

            db.Set<Quiz>().Add(quiz);
            await db.SaveChangesAsync(ct);
            quizId = quiz.Id;
        });
        return quizId;
    }

    private async Task AttachItemToModuleInDb(
        Guid moduleId,
        ModuleItemType itemType,
        Guid referenceId,
        CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            db.Set<ModuleItem>().Add(new ModuleItem(
                moduleId, itemType, referenceId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
        });
    }
}
