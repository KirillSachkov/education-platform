using System.Net;
using System.Text.Json;
using ProgressService.Contracts;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Responses;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Courses;

/// <summary>
/// Derive-модель (epic access-derive-model, Phase 1): READ-поверхности «мои курсы» /
/// last-active / learning-state выводятся из AccessService grants, а НЕ из материализованных
/// course_enrollments. Тесты доказывают, что эти эндпоинты возвращают курсы grant-holder'а
/// при ОТСУТСТВИИ enrollment-строк (фикс #366: новый курс не появлялся у lifetime-holder'ов).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class DerivedCourseReadsTests : ProgressServiceTestsBase
{
    public DerivedCourseReadsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetMyCourseProgress_ReturnsCoveredCourse_WithZeroEnrollmentRows()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Covered course",
            "Lifetime-covered, no engagement yet",
            totalModules: 0,
            totalMaterials: 2,
            totalUniqueIssues: 0,
            materialIds: [Guid.NewGuid(), Guid.NewGuid()],
            kind: "COURSE");

        // Покрытие из грантов — БЕЗ enrollment-строки.
        AccessServiceClient.AddCoveredCourse(userId, courseId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/progress/courses/my/progress?limit=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CursorResponse<UserCourseProgress> result =
            await ReadWrappedResultAsync<CursorResponse<UserCourseProgress>>(response);

        UserCourseProgress item = Assert.Single(result.Items);
        Assert.Equal(courseId, item.CourseId);
        Assert.Equal("Covered course", item.Title);
        Assert.Equal(0, item.ProgressPercent);
        Assert.Equal(0, item.CompletedItems);
        Assert.Equal(2, item.TotalMaterials);
        Assert.Equal("COURSE", item.Kind);
        Assert.Equal(Guid.Empty, item.EnrollmentId);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetMyCourseProgress_ReturnsAllCoveredCourses_WithZeroEnrollmentRows()
    {
        Guid userId = Guid.NewGuid();
        Guid courseA = Guid.NewGuid();
        Guid courseB = Guid.NewGuid();
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseA, authorId: authorA, hasFreeContent: true);
        EducationContentClient.AddCourse(courseB, authorId: authorB, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseA,
            "Course A",
            "Covered without enrollment",
            totalMaterials: 1,
            materialIds: [Guid.NewGuid()]);
        EducationContentClient.AddCourseBlueprint(
            courseB,
            "Course B",
            "Transferred course covered without enrollment",
            totalMaterials: 1,
            materialIds: [Guid.NewGuid()]);
        AccessServiceClient.AddCoveredCourse(userId, courseA);
        AccessServiceClient.AddCoveredCourse(userId, courseB);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/progress/courses/my/progress?limit=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CursorResponse<UserCourseProgress> result =
            await ReadWrappedResultAsync<CursorResponse<UserCourseProgress>>(response);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(new HashSet<Guid> { courseA, courseB }, result.Items.Select(i => i.CourseId).ToHashSet());
        Assert.All(result.Items, item => Assert.Equal(Guid.Empty, item.EnrollmentId));
    }

    [Fact]
    public async Task GetMyCourseProgress_PreservesKind_ForIntensive()
    {
        // !287 / #364: Kind пробрасывается в DTO для фронтовой дедупликации интенсивов.
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Intensive",
            "An intensive",
            totalModules: 0,
            totalMaterials: 1,
            totalUniqueIssues: 0,
            materialIds: [Guid.NewGuid()],
            kind: "INTENSIVE");
        AccessServiceClient.AddCoveredCourse(userId, courseId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/progress/courses/my/progress?limit=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CursorResponse<UserCourseProgress> result =
            await ReadWrappedResultAsync<CursorResponse<UserCourseProgress>>(response);

        UserCourseProgress item = Assert.Single(result.Items);
        Assert.Equal("INTENSIVE", item.Kind);
    }

    [Fact]
    public async Task GetMyCourseProgress_NoCoveredCourses_ReturnsEmpty()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/progress/courses/my/progress?limit=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CursorResponse<UserCourseProgress> result =
            await ReadWrappedResultAsync<CursorResponse<UserCourseProgress>>(response);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetMyCourseProgress_FiltersByAuthor()
    {
        // authorId param фильтрует covered-набор — mock возвращает уже отфильтрованный набор
        // (handler передаёт authorId в AccessService). Проверяем проброс: фильтр по автору A.
        Guid userId = Guid.NewGuid();
        Guid courseA = Guid.NewGuid();
        Guid authorA = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseA, authorId: authorA, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseA, "Author A course", "desc", totalMaterials: 1, materialIds: [Guid.NewGuid()]);
        AccessServiceClient.AddCoveredCourse(userId, courseA);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/my/progress?limit=20&authorId={authorA}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CursorResponse<UserCourseProgress> result =
            await ReadWrappedResultAsync<CursorResponse<UserCourseProgress>>(response);

        UserCourseProgress item = Assert.Single(result.Items);
        Assert.Equal(courseA, item.CourseId);
    }

    [Fact]
    public async Task GetLastActiveCourse_FallsBackToCoveredCourse_WithoutPositionOrEnrollment()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Covered last-active",
            "no position, no enrollment",
            totalMaterials: 1,
            materialIds: [Guid.NewGuid()]);
        AccessServiceClient.AddCoveredCourse(userId, courseId);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/progress/courses/my/last-active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        LastActiveCourseResponse? dto =
            await ReadWrappedResultAsync<LastActiveCourseResponse?>(response);

        Assert.NotNull(dto);
        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal("Covered last-active", dto.Title);
        Assert.Equal(0, dto.ProgressPercent);
        Assert.Null(dto.LastPosition);
        Assert.Equal(Guid.Empty, dto.EnrollmentId);
        Assert.Equal("COURSE", dto.Kind);
    }

    [Fact]
    public async Task GetLastActiveCourse_NoPositionNoCovered_ReturnsNull()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/progress/courses/my/last-active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        Assert.True(
            root.TryGetProperty("result", out JsonElement resultElement)
            && resultElement.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task GetCourseLearningState_EntitledButNoProgress_ReturnsZeroedState()
    {
        // GrantAll FakeEntitlementChecker (factory default) → пользователь entitled.
        // Нет enrollment-строки → раньше был null; теперь zeroed state (0%, not started).
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Entitled course",
            "no enrollment yet",
            totalModules: 1,
            totalMaterials: 3,
            totalUniqueIssues: 1,
            materialIds: [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()]);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/learning-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseLearningStateDto? dto = await ReadWrappedResultAsync<CourseLearningStateDto?>(response);

        Assert.NotNull(dto);
        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(Guid.Empty, dto.EnrollmentId);
        Assert.Equal(3, dto.Summary.MaterialsTotal);
        Assert.Equal(0, dto.Summary.MaterialsViewed);
        Assert.Equal(1, dto.Summary.TotalModules);
        Assert.Equal(1, dto.Summary.IssuesTotal);
        Assert.Equal(4, dto.Summary.TotalItems);
        Assert.Equal(0, dto.Summary.CompletedItems);
        Assert.Equal(0, dto.Summary.ProgressPercent);
        Assert.Empty(dto.Issues);
        Assert.Empty(dto.Materials);
    }

    [Fact]
    public async Task GetCourseLearningState_EntitledNoProgress_WithQuizzesInBlueprint_Returns200NotServerError()
    {
        // Регрессия #590: zeroed-state-ветка (BuildZeroedStateOrNull) делает ВТОРОЙ Dapper-запрос
        // на той же connection — passed-quiz lookup, который выполняется ТОЛЬКО при наличии квизов
        // в blueprint. Раньше его звали из-под ещё открытого GridReader'а enrollment-запроса →
        // Npgsql «A command is already in progress» → 500 → фронт получал undefined learningState
        // → hasActiveEnrollment=false → кнопки «Отметить изученным»/«Взять в работу» неактивны.
        // Существующий тест выше квизов в blueprint не клал, поэтому путь не покрывался.
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid quizId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Entitled course with quiz",
            "no enrollment yet, blueprint contains a quiz",
            totalModules: 1,
            totalMaterials: 2,
            totalUniqueIssues: 1,
            materialIds: [Guid.NewGuid(), Guid.NewGuid()],
            totalQuizzes: 1,
            quizIds: [quizId]);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/learning-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseLearningStateDto? dto = await ReadWrappedResultAsync<CourseLearningStateDto?>(response);

        Assert.NotNull(dto);
        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(Guid.Empty, dto.EnrollmentId);
        // TotalItems = materials(2) + issues(1) + quizzes(1)
        Assert.Equal(4, dto.Summary.TotalItems);
        Assert.Equal(0, dto.Summary.CompletedItems);
        Assert.Equal(0, dto.Summary.ProgressPercent);
        // Нет passed-попыток квиза → пустой список (а не 500 от конфликта на connection).
        Assert.Empty(dto.PassedQuizIds);
    }

    [Fact]
    public async Task GetMyEnrollment_EntitledButNoRow_ReturnsSyntheticZeroedEnrollment()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/my-enrollment");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseEnrollmentProgressDto? dto =
            await ReadWrappedResultAsync<CourseEnrollmentProgressDto?>(response);

        Assert.NotNull(dto);
        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(Guid.Empty, dto.EnrollmentId);
        Assert.Equal(0, dto.MaterialsViewed);
        Assert.Equal(0, dto.IssuesCompleted);
    }
}
