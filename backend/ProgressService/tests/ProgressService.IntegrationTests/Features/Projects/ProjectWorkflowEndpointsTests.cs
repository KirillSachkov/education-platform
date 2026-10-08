using System.Net;
using Microsoft.EntityFrameworkCore;
using ProgressService.Domain.Projects;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Projects;

[Collection(nameof(IntegrationTestsFixture))]
public class ProjectWorkflowEndpointsTests : ProgressServiceTestsBase
{
    public ProjectWorkflowEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task StartProjectWork_ShouldCreateProjectProgress()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddProject(courseId, projectId, 2);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/start");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ProjectProgress? projectProgress = await ExecuteInDb(dbContext =>
            dbContext.ProjectProgresses.FirstOrDefaultAsync(x => x.ProjectId == projectId));

        Assert.NotNull(projectProgress);
        Assert.Equal(ProjectProgressStatus.IN_PROGRESS, projectProgress.Status);
        Assert.Equal(2, projectProgress.TotalIssuesCount);
        Assert.Equal(0, projectProgress.TotalIssuesCompleted);
    }

    [Fact]
    public async Task StartProjectWork_RepeatedCall_ShouldBeIdempotent()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddProject(courseId, projectId, 2);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage firstResponse = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/start");
        HttpResponseMessage secondResponse = await PostAsync(
            $"/progress/courses/{courseId}/projects/{projectId}/start");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        int progressCount = await ExecuteInDb(dbContext =>
            dbContext.ProjectProgresses.CountAsync(x => x.ProjectId == projectId));

        Assert.Equal(1, progressCount);
    }

    private Task EnrollAsync(Guid courseId, Guid userId) => SeedEnrollmentAsync(courseId, userId);
}
