using System.Net;
using EducationContentService.Contracts.Quizzes;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Courses;

[Collection(nameof(IntegrationTestsFixture))]
public class CourseLearningStateEndpointsTests : ProgressServiceTestsBase
{
    public CourseLearningStateEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetCourseLearningState_ShouldReturnBlueprintTotalsStatusesAndLatestSubmission()
    {
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(studentId, "platform-admin");
        SeedLearningCourse(courseId, moduleId, lessonId, projectId, issueId);

        await EnrollAsync(courseId, studentId);
        await PostAsync($"/progress/materials/{lessonId}/view");
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/learning-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseLearningStateDto? dto = await ReadWrappedResultAsync<CourseLearningStateDto?>(response);

        Assert.NotNull(dto);
        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(1, dto.Summary.TotalModules);
        Assert.Equal(1, dto.Summary.MaterialsTotal);
        Assert.Equal(1, dto.Summary.MaterialsViewed);
        Assert.Equal(1, dto.Summary.IssuesTotal);
        Assert.Equal(0, dto.Summary.IssuesCompleted);
        Assert.Equal(2, dto.Summary.TotalItems);
        Assert.Equal(1, dto.Summary.CompletedItems);
        Assert.Equal(50, dto.Summary.ProgressPercent);

        MaterialLearningItemDto material = Assert.Single(dto.Materials);
        Assert.Equal(lessonId, material.MaterialId);
        Assert.Equal("VIEWED", material.Status);

        IssueLearningItemDto issue = Assert.Single(dto.Issues);
        Assert.Equal(issueId, issue.IssueId);
        Assert.Equal("UNDER_REVIEW", issue.Status);
        Assert.NotNull(issue.LatestSubmission);
        Assert.Equal("https://github.com/example/repo/pull/1", issue.LatestSubmission.Payload);
        Assert.Equal("PENDING", issue.LatestSubmission.ReviewStatus);
    }

    [Fact]
    public async Task GetCourseLearningState_ShouldReturnRequestedChangesFeedback()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedLearningCourse(courseId, moduleId, lessonId, projectId, issueId);

        AuthenticateAs(studentId, "platform-admin");
        await EnrollAsync(courseId, studentId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/2"));
        SubmitIssueResponse submission = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submission.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submission.SubmissionId}/request-changes",
            new RequestIssueChangesRequest("Нужно поправить тесты"));

        AuthenticateAs(studentId, "platform-admin");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/learning-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseLearningStateDto? dto = await ReadWrappedResultAsync<CourseLearningStateDto?>(response);
        IssueLearningItemDto issue = Assert.Single(dto!.Issues);

        Assert.Equal("REQUESTED_CHANGES", issue.Status);
        Assert.NotNull(issue.LatestSubmission);
        Assert.Equal("CHANGES_REQUESTED", issue.LatestSubmission.ReviewStatus);
        Assert.Equal("Нужно поправить тесты", issue.LatestSubmission.Feedback);
    }

    [Fact]
    public async Task GetCourseLearningState_ShouldReturnCompletedIssueAfterApprove()
    {
        Guid studentId = Guid.NewGuid();
        Guid reviewerId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedLearningCourse(courseId, moduleId, lessonId, projectId, issueId);

        AuthenticateAs(studentId, "platform-admin");
        await EnrollAsync(courseId, studentId);
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        HttpResponseMessage submitResponse = await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/3"));
        SubmitIssueResponse submission = await ReadWrappedResultAsync<SubmitIssueResponse>(submitResponse);

        AuthenticateAs(reviewerId, "platform-admin");
        await PostAsync($"/progress/courses/{courseId}/reviews/issues/{submission.SubmissionId}/start-review");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/reviews/issues/{submission.SubmissionId}/approve",
            new ApproveIssueRequest("Работа принята"));

        AuthenticateAs(studentId, "platform-admin");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/learning-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        CourseLearningStateDto? dto = await ReadWrappedResultAsync<CourseLearningStateDto?>(response);
        IssueLearningItemDto issue = Assert.Single(dto!.Issues);

        Assert.Equal("COMPLETED", issue.Status);
        Assert.NotNull(issue.LatestSubmission);
        Assert.Equal("APPROVED", issue.LatestSubmission.ReviewStatus);
        Assert.Equal("Работа принята", issue.LatestSubmission.Feedback);
        Assert.Equal(1, dto.Summary.IssuesCompleted);
    }

    [Fact]
    public async Task GetCourseLearningState_CountsMaterialFromBlueprint_NotInAnyModule()
    {
        // Регрессия #40: материал из «Ленты курса» или из подборки попадает в blueprint.MaterialIds,
        // но НЕ в module_items. После переключения source-of-truth на material_views этот клик
        // должен засчитываться в materialsViewed/Total и в процент завершения курса.
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid courseFeedMaterialId = Guid.NewGuid();
        Guid collectionMaterialId = Guid.NewGuid();

        AuthenticateAs(studentId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Course with feed and collection",
            "Демо без модулей",
            totalModules: 0,
            totalMaterials: 2,
            totalUniqueIssues: 0,
            materialIds: [courseFeedMaterialId, collectionMaterialId]);

        await EnrollAsync(courseId, studentId);
        await PostAsync($"/progress/materials/{courseFeedMaterialId}/view");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/learning-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CourseLearningStateDto? dto = await ReadWrappedResultAsync<CourseLearningStateDto?>(response);

        Assert.NotNull(dto);
        Assert.Equal(2, dto.Summary.MaterialsTotal);
        Assert.Equal(1, dto.Summary.MaterialsViewed);
        Assert.Equal(0, dto.Summary.IssuesTotal);
        Assert.Equal(2, dto.Summary.TotalItems);
        Assert.Equal(1, dto.Summary.CompletedItems);
        Assert.Equal(50, dto.Summary.ProgressPercent);

        MaterialLearningItemDto material = Assert.Single(dto.Materials);
        Assert.Equal(courseFeedMaterialId, material.MaterialId);
        Assert.Equal("VIEWED", material.Status);
    }

    [Fact]
    public async Task GetCourseLearningState_IgnoresViews_OfMaterialsNotInCourse()
    {
        // material_views — глобальная user-scoped таблица. Просмотр материала, который НЕ
        // принадлежит этому курсу (нет в blueprint.MaterialIds), не должен влиять на счётчик.
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid courseMaterialId = Guid.NewGuid();
        Guid foreignMaterialId = Guid.NewGuid();

        AuthenticateAs(studentId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Course",
            "Demo",
            totalModules: 0,
            totalMaterials: 1,
            totalUniqueIssues: 0,
            materialIds: [courseMaterialId]);

        await EnrollAsync(courseId, studentId);
        await PostAsync($"/progress/materials/{foreignMaterialId}/view");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/learning-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CourseLearningStateDto? dto = await ReadWrappedResultAsync<CourseLearningStateDto?>(response);

        Assert.NotNull(dto);
        Assert.Equal(1, dto.Summary.MaterialsTotal);
        Assert.Equal(0, dto.Summary.MaterialsViewed);
        Assert.Empty(dto.Materials);
    }

    [Fact]
    public async Task GetCourseLearningState_ReturnsPassedQuizIds_AndCountsThemInProgress()
    {
        // ST-16 #495: quiz-строки программы курса рисуют галочку «пройден» по PassedQuizIds —
        // distinct passed quiz_attempts ∩ blueprint.QuizIds. Непройденный квиз в список не входит.
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid passedQuizId = Guid.NewGuid();
        Guid failedQuizId = Guid.NewGuid();
        Guid questionId = Guid.NewGuid();
        Guid correctOptionId = Guid.NewGuid();
        Guid wrongOptionId = Guid.NewGuid();

        AuthenticateAs(studentId, "platform-participant");
        SeedSingleChoiceQuizAnswerKey(passedQuizId, questionId, correctOptionId);
        SeedSingleChoiceQuizAnswerKey(failedQuizId, questionId, correctOptionId);
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Course with quizzes",
            "Demo",
            totalModules: 1,
            totalQuizzes: 2,
            quizIds: [passedQuizId, failedQuizId]);

        await EnrollAsync(courseId, studentId);
        await PostAsJsonAsync(
            $"/progress/quizzes/{passedQuizId}/attempts",
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(questionId, [correctOptionId], null)]));
        await PostAsJsonAsync(
            $"/progress/quizzes/{failedQuizId}/attempts",
            new SubmitQuizAttemptRequest([new SubmitQuizAnswerItem(questionId, [wrongOptionId], null)]));

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/learning-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CourseLearningStateDto? dto = await ReadWrappedResultAsync<CourseLearningStateDto?>(response);

        Assert.NotNull(dto);
        Guid passed = Assert.Single(dto.PassedQuizIds);
        Assert.Equal(passedQuizId, passed);
        Assert.Equal(2, dto.Summary.TotalItems);
        Assert.Equal(1, dto.Summary.CompletedItems);
        Assert.Equal(50, dto.Summary.ProgressPercent);
    }

    private void SeedSingleChoiceQuizAnswerKey(Guid quizId, Guid questionId, Guid correctOptionId)
    {
        // Ключ несёт только правильные варианты — submit с любым другим optionId
        // грейдится как неверный (0%, passed=false).
        EducationContentClient.AddQuizAnswerKey(new QuizAnswerKeyDto(
            quizId,
            "MATERIAL_CHECK",
            PassingScorePercent: 70,
            [
                new QuizAnswerKeyQuestionDto(
                    questionId, "SINGLE_CHOICE", "Вопрос", null, null, [correctOptionId], null),
            ],
            LevelTestConfig: null,
            AccessType: "PUBLIC"));
    }

    private void SeedLearningCourse(Guid courseId, Guid moduleId, Guid lessonId, Guid projectId, Guid issueId)
    {
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddModule(courseId, moduleId, 2);
        EducationContentClient.AddMaterial(moduleId, lessonId, 2);
        EducationContentClient.AddMaterialCourseContext(lessonId, courseId, moduleId, moduleItemsTotal: 2);
        EducationContentClient.AddProject(courseId, projectId, 1);
        EducationContentClient.AddIssue(projectId, issueId, moduleId);
        EducationContentClient.AddCourseBlueprint(
            courseId,
            "Demo course",
            "Demo description",
            totalModules: 1,
            totalMaterials: 1,
            totalUniqueIssues: 1,
            materialIds: [lessonId]);
    }

    private Task EnrollAsync(Guid courseId, Guid userId) => SeedEnrollmentAsync(courseId, userId);
}
