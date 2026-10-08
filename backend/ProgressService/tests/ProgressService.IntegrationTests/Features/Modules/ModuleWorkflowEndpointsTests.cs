using System.Net;
using Microsoft.EntityFrameworkCore;
using ProgressService.Domain.Modules;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.Modules;

[Collection(nameof(IntegrationTestsFixture))]
public class ModuleWorkflowEndpointsTests : ProgressServiceTestsBase
{
    public ModuleWorkflowEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task StartModuleWork_ShouldCreateModuleProgress()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddModule(courseId, moduleId, 3);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage response = await PostAsync(
            $"/progress/courses/{courseId}/modules/{moduleId}/start");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        ModuleProgress? moduleProgress = await ExecuteInDb(dbContext =>
            dbContext.ModuleProgresses.FirstOrDefaultAsync(x => x.ModuleId == moduleId));

        Assert.NotNull(moduleProgress);
        Assert.Equal(ModuleProgressStatus.IN_PROGRESS, moduleProgress.Status);
        Assert.Equal(3, moduleProgress.ItemsTotal);
        Assert.Equal(0, moduleProgress.ItemsCompleted);
    }

    [Fact]
    public async Task StartModuleWork_RepeatedCall_ShouldBeIdempotent()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-admin");
        EducationContentClient.AddCourse(courseId, hasFreeContent: true);
        EducationContentClient.AddModule(courseId, moduleId, 3);

        await EnrollAsync(courseId, userId);

        HttpResponseMessage firstResponse = await PostAsync(
            $"/progress/courses/{courseId}/modules/{moduleId}/start");
        HttpResponseMessage secondResponse = await PostAsync(
            $"/progress/courses/{courseId}/modules/{moduleId}/start");

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        int progressCount = await ExecuteInDb(dbContext =>
            dbContext.ModuleProgresses.CountAsync(x => x.ModuleId == moduleId));

        Assert.Equal(1, progressCount);
    }

    private Task EnrollAsync(Guid courseId, Guid userId) => SeedEnrollmentAsync(courseId, userId);
}
