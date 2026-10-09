using System.Net;
using EducationContentService.Core.Features.Quizzes.Queries;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Materials;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>Список квизов автора с фильтрами и проверкой доступа.</summary>
[Collection(nameof(IntegrationTestsFixture))]
public class GetMyQuizzesTests : EducationContentServiceTestsBase
{
    private static readonly Guid _optionA = Guid.NewGuid();
    private static readonly Guid _optionB = Guid.NewGuid();

    public GetMyQuizzesTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetMyQuizzes_ReturnsOwnQuizzesNewestFirst_WithCounts_HidesForeign()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        Guid otherAuthorId = Guid.NewGuid();

        Guid usedQuizId = await SeedQuizAsync(
            authorId, publish: true, AccessType.ENROLLED, QuizPurpose.MATERIAL_CHECK, "Используемый квиз");
        await SeedCourseBindingAsync(authorId, usedQuizId);
        await SeedMaterialWithQuizAsync(authorId, usedQuizId);
        await SeedMaterialWithQuizAsync(authorId, usedQuizId);

        Guid draftQuizId = await SeedQuizAsync(
            authorId, publish: false, AccessType.PUBLIC, QuizPurpose.MATERIAL_CHECK, "Черновик квиза");

        await SeedQuizAsync(
            otherAuthorId, publish: true, AccessType.PUBLIC, QuizPurpose.MATERIAL_CHECK, "Чужой квиз");

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/mine", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<MyQuizSummaryDto> quizzes = await ReadResultAsync<List<MyQuizSummaryDto>>(response);

        Assert.Equal(2, quizzes.Count);

        // Newest-first: черновик создан позже.
        Assert.Equal(draftQuizId, quizzes[0].Id);
        Assert.Equal("DRAFT", quizzes[0].Status);
        Assert.Equal("MATERIAL_CHECK", quizzes[0].Purpose);
        Assert.Equal(0, quizzes[0].UsedByMaterialsCount);
        Assert.Equal(0, quizzes[0].CourseCount);

        MyQuizSummaryDto used = quizzes[1];
        Assert.Equal(usedQuizId, used.Id);
        Assert.Equal("Используемый квиз", used.Title);
        Assert.Equal("PUBLISHED", used.Status);
        Assert.Equal("ENROLLED", used.AccessType);
        Assert.Equal("MATERIAL_CHECK", used.Purpose);
        Assert.Equal(2, used.QuestionsCount);
        Assert.Equal(Quiz.DEFAULT_PASSING_SCORE_PERCENT, used.PassingScorePercent);
        Assert.Equal(2, used.UsedByMaterialsCount);
        Assert.Equal(1, used.CourseCount);
    }

    [Fact]
    public async Task GetMyQuizzes_Admin_SeesAllAuthorsQuizzes()
    {
        CancellationToken ct = CancellationToken.None;
        Guid quizAId = await SeedQuizAsync(
            Guid.NewGuid(), publish: true, AccessType.PUBLIC, QuizPurpose.MATERIAL_CHECK, "Квиз автора A");
        Guid quizBId = await SeedQuizAsync(
            Guid.NewGuid(), publish: false, AccessType.PUBLIC, QuizPurpose.MATERIAL_CHECK, "Квиз автора B");

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/mine", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        IReadOnlyList<MyQuizSummaryDto> quizzes = await ReadResultAsync<List<MyQuizSummaryDto>>(response);
        Assert.Contains(quizzes, q => q.Id == quizAId);
        Assert.Contains(quizzes, q => q.Id == quizBId);
    }

    [Fact]
    public async Task GetMyQuizzes_Participant_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/quizzes/mine", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ===== Helpers =====

    private async Task<Guid> SeedQuizAsync(
        Guid authorId, bool publish, AccessType accessType, QuizPurpose purpose, string title)
    {
        Guid quizId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            Quiz quiz = Quiz.Create(
                authorId,
                Title.Create(title).Value,
                BuildDomainQuestions(),
                purpose: purpose,
                accessType: accessType).Value;

            if (publish)
                quiz.Publish();

            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            quizId = quiz.Id;
        });
        return quizId;
    }

    private async Task SeedCourseBindingAsync(Guid authorId, Guid quizId)
    {
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {Guid.NewGuid():N}").Value,
                Description.Create("Описание").Value,
                CourseSlug.Create($"course-{Guid.CreateVersion7():N}").Value,
                SortKey.Initial());
            db.Courses.Add(course);
            db.CourseQuizzes.Add(new CourseQuiz(course.Id, quizId, SortKey.Initial()));
            await db.SaveChangesAsync();
        });
    }

    private async Task SeedMaterialWithQuizAsync(Guid authorId, Guid quizId)
    {
        await ExecuteInDb(async db =>
        {
            var material = new Material(
                authorId,
                Title.Create($"Материал {Guid.NewGuid():N}").Value,
                MaterialKind.ARTICLE,
                AccessType.PUBLIC);
            material.AttachQuiz(quizId);
            db.Materials.Add(material);
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