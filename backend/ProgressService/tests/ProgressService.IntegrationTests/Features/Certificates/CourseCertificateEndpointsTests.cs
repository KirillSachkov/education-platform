using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Certificates;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Certificates;

/// <summary>
///     Сертификаты о прохождении курса (#467): идемпотентный claim на ≥80% completion
///     (материалы + задания + тесты суммарно от blueprint.TotalItems, #650), публичная
///     анонимная страница проверки по id, список «мои сертификаты».
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class CourseCertificateEndpointsTests : ProgressServiceTestsBase
{
    public CourseCertificateEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task Claim_WithoutAuth_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await PostAsync(ClaimUrl(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMy_WithoutAuth_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/certificates/my");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Claim_WithoutCourseEntitlement_Returns403()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        AuthenticateAs(studentId, "platform-participant");
        EntitlementChecker.DenyAll();

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Claim_FullyCompletedCourse_IssuesCertificateWithSnapshots()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid material1 = Guid.NewGuid();
        Guid material2 = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        await SeedFullyCompletedCourseAsync(studentId, courseId, [material1, material2], issueId,
            courseTitle: "Архитектура .NET");
        AuthServiceClient.AddUser(studentId, "ivan@example.com", name: "Иван Иванов", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CourseCertificateResponse certificate =
            await ReadWrappedResultAsync<CourseCertificateResponse>(response);

        Assert.NotEqual(Guid.Empty, certificate.Id);
        Assert.StartsWith("CERT-", certificate.SerialNumber, StringComparison.Ordinal);
        Assert.Equal(21, certificate.SerialNumber.Length);
        Assert.Equal("Иван Иванов", certificate.HolderName);
        Assert.Equal("Архитектура .NET", certificate.CourseTitle);
        Assert.Equal(courseId, certificate.CourseId);
        Assert.NotEqual(default, certificate.IssuedAt);

        CourseCertificate? stored = await ExecuteInDb(db =>
            db.CourseCertificates.FirstOrDefaultAsync(c => c.Id == certificate.Id));
        Assert.NotNull(stored);
        Assert.Equal(studentId, stored.UserId);
        Assert.Equal(certificate.SerialNumber, stored.SerialNumber);
    }

    [Fact]
    public async Task Claim_PartialProgress_Returns400NotCompleted()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid material1 = Guid.NewGuid();
        Guid material2 = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Demo course",
            "Demo description",
            totalMaterials: 2,
            materialIds: [material1, material2]);
        AuthServiceClient.AddUser(studentId, "ivan@example.com", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        // Изучен только один материал из двух.
        await PostAsync($"/progress/materials/{material1}/view");

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("progress.certificate.course.not.completed", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Claim_MaterialsDoneButIssuesPending_Returns400NotCompleted()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        // Kind=COURSE с заданием: материалы изучены, задание не COMPLETED.
        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Demo course",
            "Demo description",
            totalMaterials: 1,
            totalUniqueIssues: 1,
            materialIds: [materialId]);
        AuthServiceClient.AddUser(studentId, "ivan@example.com", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        await PostAsync($"/progress/materials/{materialId}/view");

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("progress.certificate.course.not.completed", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Claim_Twice_IsIdempotentAndReturnsSameSerial()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        await SeedFullyCompletedCourseAsync(studentId, courseId, [materialId], issueId);
        AuthServiceClient.AddUser(studentId, "ivan@example.com", name: "Иван Иванов", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        HttpResponseMessage firstResponse = await PostAsync(ClaimUrl(courseId));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        CourseCertificateResponse first =
            await ReadWrappedResultAsync<CourseCertificateResponse>(firstResponse);

        HttpResponseMessage secondResponse = await PostAsync(ClaimUrl(courseId));
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        CourseCertificateResponse second =
            await ReadWrappedResultAsync<CourseCertificateResponse>(secondResponse);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.SerialNumber, second.SerialNumber);

        int storedCount = await ExecuteInDb(db =>
            db.CourseCertificates.CountAsync(c => c.UserId == studentId && c.CourseId == courseId));
        Assert.Equal(1, storedCount);
    }

    [Fact]
    public async Task Claim_IntensiveWithAllMaterialsViewed_IssuesCertificate()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        // INTENSIVE — заданий нет by design, completion считается только по материалам.
        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Интенсив по Docker",
            "Demo description",
            totalMaterials: 1,
            materialIds: [materialId],
            kind: "INTENSIVE");
        AuthServiceClient.AddUser(studentId, "ivan@example.com", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        await PostAsync($"/progress/materials/{materialId}/view");

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CourseCertificateResponse certificate =
            await ReadWrappedResultAsync<CourseCertificateResponse>(response);
        Assert.Equal("Интенсив по Docker", certificate.CourseTitle);
    }

    [Fact]
    public async Task Claim_MaterialsDoneButQuizNotPassed_Returns400NotCompleted()
    {
        // Квизы в блюпринте участвуют в completion-чеке для ВСЕХ Kind (ST-13 #493).
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid quizId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Demo course",
            "Demo description",
            totalMaterials: 1,
            materialIds: [materialId],
            totalQuizzes: 1,
            quizIds: [quizId]);
        AuthServiceClient.AddUser(studentId, "ivan@example.com", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        await PostAsync($"/progress/materials/{materialId}/view");

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("progress.certificate.course.not.completed", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Claim_MaterialsAndQuizPassed_IssuesCertificate()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid quizId = Guid.NewGuid();
        Guid questionId = Guid.NewGuid();
        Guid correctOptionId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Demo course",
            "Demo description",
            totalMaterials: 1,
            materialIds: [materialId],
            totalQuizzes: 1,
            quizIds: [quizId]);
        EducationContentClient.AddQuizAnswerKey(new EducationContentService.Contracts.Quizzes.QuizAnswerKeyDto(
            quizId,
            "MATERIAL_CHECK",
            PassingScorePercent: 70,
            [
                new EducationContentService.Contracts.Quizzes.QuizAnswerKeyQuestionDto(
                    questionId, "SINGLE_CHOICE", "Вопрос", null, null, [correctOptionId], null),
            ],
            LevelTestConfig: null));
        AuthServiceClient.AddUser(studentId, "ivan@example.com", name: "Иван Иванов", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        await PostAsync($"/progress/materials/{materialId}/view");

        // Failed-попытка (мимо проходного) сертификат не разблокирует...
        HttpResponseMessage failedAttempt = await PostAsJsonAsync(
            $"/progress/quizzes/{quizId}/attempts",
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(questionId, [Guid.NewGuid()], null)]));
        Assert.Equal(HttpStatusCode.OK, failedAttempt.StatusCode);

        HttpResponseMessage blockedClaim = await PostAsync(ClaimUrl(courseId));
        Assert.Equal(HttpStatusCode.BadRequest, blockedClaim.StatusCode);

        // ...а passed — да.
        HttpResponseMessage passedAttempt = await PostAsJsonAsync(
            $"/progress/quizzes/{quizId}/attempts",
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(questionId, [correctOptionId], null)]));
        Assert.Equal(HttpStatusCode.OK, passedAttempt.StatusCode);

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CourseCertificateResponse certificate =
            await ReadWrappedResultAsync<CourseCertificateResponse>(response);
        Assert.Equal("Demo course", certificate.CourseTitle);
    }

    [Fact]
    public async Task Claim_CourseWithoutMaterials_Returns400Empty()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Пустой курс",
            "Demo description",
            totalMaterials: 0);
        AuthServiceClient.AddUser(studentId, "ivan@example.com", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("progress.certificate.course.empty", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Claim_WithoutDisplayName_FallsBackToUsername()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Demo course",
            "Demo description",
            totalMaterials: 1,
            materialIds: [materialId]);
        AuthServiceClient.AddUser(studentId, "student@example.com", name: null, username: "student-42");

        AuthenticateAs(studentId, "platform-participant");
        await PostAsync($"/progress/materials/{materialId}/view");

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CourseCertificateResponse certificate =
            await ReadWrappedResultAsync<CourseCertificateResponse>(response);
        Assert.Equal("student-42", certificate.HolderName);
    }

    [Fact]
    public async Task GetById_Anonymous_Returns200WithSnapshots()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid materialId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        await SeedFullyCompletedCourseAsync(studentId, courseId, [materialId], issueId,
            courseTitle: "Архитектура .NET");
        AuthServiceClient.AddUser(studentId, "ivan@example.com", name: "Иван Иванов", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        HttpResponseMessage claimResponse = await PostAsync(ClaimUrl(courseId));
        Assert.Equal(HttpStatusCode.OK, claimResponse.StatusCode);
        CourseCertificateResponse claimed =
            await ReadWrappedResultAsync<CourseCertificateResponse>(claimResponse);

        RemoveAuthentication();
        HttpResponseMessage response = await AppHttpClient.GetAsync($"/progress/certificates/{claimed.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CourseCertificateResponse certificate =
            await ReadWrappedResultAsync<CourseCertificateResponse>(response);
        Assert.Equal(claimed.Id, certificate.Id);
        Assert.Equal(claimed.SerialNumber, certificate.SerialNumber);
        Assert.Equal("Иван Иванов", certificate.HolderName);
        Assert.Equal("Архитектура .NET", certificate.CourseTitle);
    }

    [Fact]
    public async Task GetById_Unknown_Returns404()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/progress/certificates/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMy_ReturnsOwnCertificatesNewestFirst()
    {
        Guid studentId = Guid.NewGuid();
        Guid course1 = Guid.NewGuid();
        Guid course2 = Guid.NewGuid();
        Guid material1 = Guid.NewGuid();
        Guid material2 = Guid.NewGuid();

        AuthServiceClient.AddUser(studentId, "ivan@example.com", name: "Иван Иванов", username: "ivan");
        foreach ((Guid courseId, Guid materialId, string title) in new[]
                 {
                     (course1, material1, "Первый курс"),
                     (course2, material2, "Второй курс"),
                 })
        {
            EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
            EducationContentClient.AddCourseBlueprint(
                courseId,
                title,
                "Demo description",
                totalMaterials: 1,
                materialIds: [materialId]);
        }

        AuthenticateAs(studentId, "platform-participant");
        await PostAsync($"/progress/materials/{material1}/view");
        await PostAsync($"/progress/materials/{material2}/view");

        HttpResponseMessage firstClaim = await PostAsync(ClaimUrl(course1));
        Assert.Equal(HttpStatusCode.OK, firstClaim.StatusCode);
        HttpResponseMessage secondClaim = await PostAsync(ClaimUrl(course2));
        Assert.Equal(HttpStatusCode.OK, secondClaim.StatusCode);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/progress/certificates/my");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        List<CourseCertificateResponse> items =
            await ReadWrappedResultAsync<List<CourseCertificateResponse>>(response);

        Assert.Equal(2, items.Count);
        Assert.Equal("Второй курс", items[0].CourseTitle);
        Assert.Equal("Первый курс", items[1].CourseTitle);
        Assert.True(items[0].IssuedAt >= items[1].IssuedAt);
    }

    [Fact]
    public async Task Claim_AtEightyPercentThreshold_IssuesCertificate()
    {
        // #650: порог выдачи — ≥80% всех элементов программы. 4 из 5 материалов = ровно 80%.
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid[] materialIds =
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId, "Demo course", "Demo description", totalMaterials: 5, materialIds: materialIds);
        AuthServiceClient.AddUser(studentId, "ivan@example.com", name: "Иван Иванов", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        foreach (Guid materialId in materialIds[..4])
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await PostAsync($"/progress/materials/{materialId}/view")).StatusCode);
        }

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Claim_JustBelowEightyPercent_Returns400NotCompleted()
    {
        // 7 из 9 материалов ≈ 77.7% < 80% → не выдаём (граница чуть ниже порога).
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid[] materialIds = Enumerable.Range(0, 9).Select(_ => Guid.NewGuid()).ToArray();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId, "Demo course", "Demo description", totalMaterials: 9, materialIds: materialIds);
        AuthServiceClient.AddUser(studentId, "ivan@example.com", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        foreach (Guid materialId in materialIds[..7])
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await PostAsync($"/progress/materials/{materialId}/view")).StatusCode);
        }

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("progress.certificate.course.not.completed", await ReadErrorCodeAsync(response));
    }

    [Fact]
    public async Task Claim_EightyPercentSkippingIssueCategory_IssuesCertificate()
    {
        // #650: 80% СУММАРНО — можно пропустить целую категорию. 3 материала + 1 пройденный
        // тест из 5 элементов (3 материала + 1 задание + 1 тест) = 80%; задание НЕ сдано.
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid[] materialIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        Guid quizId = Guid.NewGuid();
        Guid questionId = Guid.NewGuid();
        Guid correctOptionId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId, "Demo course", "Demo description",
            totalMaterials: 3, materialIds: materialIds,
            totalUniqueIssues: 1,
            totalQuizzes: 1, quizIds: [quizId]);
        EducationContentClient.AddQuizAnswerKey(new EducationContentService.Contracts.Quizzes.QuizAnswerKeyDto(
            quizId,
            "MATERIAL_CHECK",
            PassingScorePercent: 70,
            [
                new EducationContentService.Contracts.Quizzes.QuizAnswerKeyQuestionDto(
                    questionId, "SINGLE_CHOICE", "Вопрос", null, null, [correctOptionId], null),
            ],
            LevelTestConfig: null));
        AuthServiceClient.AddUser(studentId, "ivan@example.com", name: "Иван Иванов", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        foreach (Guid materialId in materialIds)
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await PostAsync($"/progress/materials/{materialId}/view")).StatusCode);
        }

        HttpResponseMessage passedAttempt = await PostAsJsonAsync(
            $"/progress/quizzes/{quizId}/attempts",
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(questionId, [correctOptionId], null)]));
        Assert.Equal(HttpStatusCode.OK, passedAttempt.StatusCode);

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Claim_IntensiveAtEightyPercentMaterials_IssuesCertificate()
    {
        // INTENSIVE — заданий нет by design; 80% считается по материалам (+тесты, если есть).
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid[] materialIds =
            [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId, "Интенсив по Docker", "Demo description",
            totalMaterials: 5, materialIds: materialIds, kind: "INTENSIVE");
        AuthServiceClient.AddUser(studentId, "ivan@example.com", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        foreach (Guid materialId in materialIds[..4])
        {
            Assert.Equal(
                HttpStatusCode.OK,
                (await PostAsync($"/progress/materials/{materialId}/view")).StatusCode);
        }

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Claim_QuizOnlyCourseAllPassed_IssuesCertificate()
    {
        // #650: курс без материалов, но с тестом — НЕ «пустой» (гейт TotalItems==0, не
        // TotalMaterials==0); сертифицируется по пройденным тестам.
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid quizId = Guid.NewGuid();
        Guid questionId = Guid.NewGuid();
        Guid correctOptionId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: Guid.NewGuid(), hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId, "Demo course", "Demo description",
            totalMaterials: 0, totalQuizzes: 1, quizIds: [quizId]);
        EducationContentClient.AddQuizAnswerKey(new EducationContentService.Contracts.Quizzes.QuizAnswerKeyDto(
            quizId,
            "MATERIAL_CHECK",
            PassingScorePercent: 70,
            [
                new EducationContentService.Contracts.Quizzes.QuizAnswerKeyQuestionDto(
                    questionId, "SINGLE_CHOICE", "Вопрос", null, null, [correctOptionId], null),
            ],
            LevelTestConfig: null));
        AuthServiceClient.AddUser(studentId, "ivan@example.com", username: "ivan");

        AuthenticateAs(studentId, "platform-participant");
        HttpResponseMessage passedAttempt = await PostAsJsonAsync(
            $"/progress/quizzes/{quizId}/attempts",
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(questionId, [correctOptionId], null)]));
        Assert.Equal(HttpStatusCode.OK, passedAttempt.StatusCode);

        HttpResponseMessage response = await PostAsync(ClaimUrl(courseId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string ClaimUrl(Guid courseId) =>
        $"/progress/courses/{courseId}/certificate/claim";

    /// <summary>
    ///     Полное прохождение Kind=COURSE: все материалы блюпринта отмечены изученными
    ///     студентом, задание принято стафом через mark-complete-for-user (строит полный
    ///     progress-каскад вместе с enrollment-якорем).
    /// </summary>
    private async Task SeedFullyCompletedCourseAsync(
        Guid studentId,
        Guid courseId,
        Guid[] materialIds,
        Guid issueId,
        string courseTitle = "Demo course")
    {
        Guid authorId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId, hasFreeContent: true);
        EducationContentClient.AddProject(courseId, projectId, 1);
        EducationContentClient.AddIssue(projectId, issueId);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            courseTitle,
            "Demo description",
            totalMaterials: materialIds.Length,
            totalUniqueIssues: 1,
            materialIds: materialIds);

        AuthenticateAs(studentId, "platform-participant");
        foreach (Guid materialId in materialIds)
        {
            HttpResponseMessage viewResponse = await PostAsync($"/progress/materials/{materialId}/view");
            Assert.Equal(HttpStatusCode.OK, viewResponse.StatusCode);
        }

        AuthenticateAsAdmin();
        HttpResponseMessage markCompleteResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/mark-complete-for-user",
            new MarkIssueCompleteForUserRequest(studentId));
        Assert.Equal(HttpStatusCode.OK, markCompleteResponse.StatusCode);
    }

    private static async Task<string> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        string payload = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement messages = document.RootElement.GetProperty("error").GetProperty("messages");
        return messages[0].GetProperty("code").GetString()!;
    }
}
