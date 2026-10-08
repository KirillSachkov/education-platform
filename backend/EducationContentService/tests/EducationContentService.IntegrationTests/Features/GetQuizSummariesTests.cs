using System.Net;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Quizzes;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

/// <summary>
///     S2S quiz summaries (#556): <c>POST /internal/quizzes/summaries</c> — батч
///     title/purpose/представительный курс для enrichment'а «Мои тесты» + админ-статистики
///     в ProgressService. Регрессия (#556 fix): представительный курс выбирался через
///     <c>MIN(course_id)</c>, но PostgreSQL не имеет агрегата <c>MIN(uuid)</c> → запрос падал
///     на plan-этапе при ЛЮБОМ вызове (даже standalone-квиз), из-за чего обе страницы
///     статистики тестов отдавали 500. Фикс — <c>DISTINCT ON ... ORDER BY</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class GetQuizSummariesTests : EducationContentServiceTestsBase
{
    public GetQuizSummariesTests(IntegrationTestsWebFactory factory) : base(factory) { }

    [Fact]
    public async Task GetQuizSummaries_StandaloneQuiz_Returns200WithNullCourse()
    {
        // Регрессия: до фикса этот вызов падал 500 на MIN(uuid) ещё до выполнения,
        // независимо от наличия course_quizzes — standalone-квиза достаточно.
        CancellationToken ct = CancellationToken.None;
        Guid quizId = await CreateQuizInDb("Standalone quiz", ct);

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/quizzes/summaries", new GetQuizSummariesRequest([quizId]), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<QuizSummaryLookupDto> summaries =
            await ReadResultAsync<IReadOnlyList<QuizSummaryLookupDto>>(response);

        QuizSummaryLookupDto summary = Assert.Single(summaries);
        Assert.Equal(quizId, summary.Id);
        Assert.Equal("Standalone quiz", summary.Title);
        Assert.Equal("MATERIAL_CHECK", summary.Purpose);
        Assert.Null(summary.CourseId);
    }

    [Fact]
    public async Task GetQuizSummaries_CourseBoundQuiz_ReturnsCourse()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid quizId = await CreateQuizInDb("Bound quiz", ct);
        await BindQuizToCourseInDb(courseId, quizId, ct);

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/quizzes/summaries", new GetQuizSummariesRequest([quizId]), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<QuizSummaryLookupDto> summaries =
            await ReadResultAsync<IReadOnlyList<QuizSummaryLookupDto>>(response);

        QuizSummaryLookupDto summary = Assert.Single(summaries);
        Assert.Equal(courseId, summary.CourseId);
    }

    [Fact]
    public async Task GetQuizSummaries_QuizInTwoCourses_ReturnsSingleRepresentativeRow()
    {
        // DISTINCT ON схлопывает несколько привязок в одну строку на квиз (вместо
        // дублей из JOIN). Представитель — один из связанных курсов.
        CancellationToken ct = CancellationToken.None;
        Guid courseA = await CreateCourseInDb(ct);
        Guid courseB = await CreateCourseInDb(ct);
        Guid quizId = await CreateQuizInDb("Multi-bound quiz", ct);
        await BindQuizToCourseInDb(courseA, quizId, ct);
        await BindQuizToCourseInDb(courseB, quizId, ct);

        AuthenticateAsAdmin();
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/quizzes/summaries", new GetQuizSummariesRequest([quizId]), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        IReadOnlyList<QuizSummaryLookupDto> summaries =
            await ReadResultAsync<IReadOnlyList<QuizSummaryLookupDto>>(response);

        QuizSummaryLookupDto summary = Assert.Single(summaries);
        Assert.Contains(summary.CourseId, new Guid?[] { courseA, courseB });
    }

    [Fact]
    public async Task GetQuizSummaries_AsAuthorRole_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/quizzes/summaries", new GetQuizSummariesRequest([Guid.NewGuid()]));

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

    private async Task<Guid> CreateQuizInDb(string title, CancellationToken ct)
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

            Quiz quiz = Quiz.Create(Guid.NewGuid(), Title.Create(title).Value, [question]).Value;
            UnitResult<Error> publish = quiz.Publish();
            Assert.True(publish.IsSuccess, publish.IsFailure ? publish.Error.GetMessage() : null);

            db.Set<Quiz>().Add(quiz);
            await db.SaveChangesAsync(ct);
            quizId = quiz.Id;
        });
        return quizId;
    }

    private async Task BindQuizToCourseInDb(Guid courseId, Guid quizId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            db.Set<CourseQuiz>().Add(new CourseQuiz(courseId, quizId, SortKey.Initial()));
            await db.SaveChangesAsync(ct);
        });
    }
}
