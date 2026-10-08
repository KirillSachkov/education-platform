using System.Net;
using ProgressService.Contracts.Dtos;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Courses;

[Collection(nameof(IntegrationTestsFixture))]
public class StudentCourseProgressEndpointsTests : ProgressServiceTestsBase
{
    public StudentCourseProgressEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task AuthorOfCourse_SeesStudentProgress_MaterialsAndIssueStatuses()
    {
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        SeedLearningCourse(courseId, authorId, moduleId, lessonId, projectId, issueId);

        // Студент генерирует прогресс через обычный HTTP-flow: отметить материал изученным + сдать задание.
        AuthenticateAs(studentId, "platform-admin");
        await SeedEnrollmentAsync(courseId, studentId, authorId);
        await PostAsync($"/progress/materials/{lessonId}/view");
        await PostAsync($"/progress/courses/{courseId}/projects/{projectId}/issues/{issueId}/start");
        await PostAsJsonAsync(
            $"/progress/courses/{courseId}/issues/{issueId}/submit",
            new SubmitIssueRequest("https://github.com/example/repo/pull/1"));

        // Staff (автор курса) смотрит прогресс конкретного студента.
        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students/{studentId}/progress");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        StudentCourseProgressDto dto = await ReadWrappedResultAsync<StudentCourseProgressDto>(response);

        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(studentId, dto.UserId);
        Assert.True(dto.EnrollmentStarted);
        Assert.NotNull(dto.EnrolledAt);

        StudentCompletedMaterialDto material = Assert.Single(dto.CompletedMaterials);
        Assert.Equal(lessonId, material.MaterialId);
        Assert.NotEqual(default, material.CompletedAt);

        StudentIssueProgressDto issue = Assert.Single(dto.Issues);
        Assert.Equal(issueId, issue.IssueId);
        Assert.Equal(projectId, issue.ProjectId);
        Assert.Equal("UNDER_REVIEW", issue.Status);
        Assert.Equal("PENDING", issue.ReviewStatus);
        Assert.NotNull(issue.SubmittedAt);
        Assert.Equal(1, issue.AttemptsCount);
    }

    [Fact]
    public async Task GrantHolderWithoutProgress_Returns200WithEmptyArrays_NotFound()
    {
        // access-derive-model (#367): grant-holder без прогресс-якоря. Эндпоинт обязан вернуть 200
        // с пустыми массивами («не начинал»), а не 404.
        Guid authorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
        AccessServiceClient.AddCourseGrantee(courseId, studentId);

        AuthenticateAs(authorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students/{studentId}/progress");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        StudentCourseProgressDto dto = await ReadWrappedResultAsync<StudentCourseProgressDto>(response);

        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(studentId, dto.UserId);
        Assert.False(dto.EnrollmentStarted);
        Assert.Null(dto.EnrolledAt);
        Assert.Empty(dto.CompletedMaterials);
        Assert.Empty(dto.Issues);
    }

    [Fact]
    public async Task AuthorOfAnotherCourse_CannotSeeStudentProgress_Returns403()
    {
        Guid realAuthorId = Guid.NewGuid();
        Guid strangerAuthorId = Guid.NewGuid();
        Guid studentId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();

        EducationContentClient.AddCourse(courseId, authorId: realAuthorId, hasFreeContent: true);

        AuthenticateAs(strangerAuthorId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/progress/courses/{courseId}/students/{studentId}/progress");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private void SeedLearningCourse(
        Guid courseId,
        Guid authorId,
        Guid moduleId,
        Guid lessonId,
        Guid projectId,
        Guid issueId)
    {
        EducationContentClient.AddCourse(courseId, authorId: authorId, hasFreeContent: true);
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
}
