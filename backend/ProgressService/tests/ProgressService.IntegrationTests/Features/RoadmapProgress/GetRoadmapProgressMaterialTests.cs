using System.Net;
using System.Net.Http.Json;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Materials;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.RoadmapProgress;

[Collection(nameof(IntegrationTestsFixture))]
public class GetRoadmapProgressMaterialTests : ProgressServiceTestsBase
{
    private const string ENDPOINT = "/progress/roadmap-progress";

    public GetRoadmapProgressMaterialTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetRoadmapProgress_Material_ReturnsViewedStatus()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid viewedMaterialId = Guid.NewGuid();
        Guid unviewedMaterialId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(
                userId,
                courseId,
                Guid.NewGuid(),
                EnrollmentSource.ENGAGEMENT).Value;
            db.CourseEnrollments.Add(enrollment);

            // User-scoped MaterialView: один факт на пару (user, material), живёт вне курса.
            MaterialView view = MaterialView.Create(userId, viewedMaterialId).Value;
            db.MaterialViews.Add(view);

            await db.SaveChangesAsync();
        });

        var request = new GetRoadmapProgressRequest([
            new RoadmapProgressItemRequest("Material", viewedMaterialId, courseId),
            new RoadmapProgressItemRequest("Material", unviewedMaterialId, courseId),
        ]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(ENDPOINT, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetRoadmapProgressResponse result =
            await ReadWrappedResultAsync<GetRoadmapProgressResponse>(response);

        RoadmapProgressItemDto viewedDto = Assert.Single(result.Items, i => i.EntityId == viewedMaterialId);
        Assert.Equal("VIEWED", viewedDto.Status);
        Assert.Equal(courseId, viewedDto.CourseId);
        Assert.Equal("Material", viewedDto.EntityType);

        RoadmapProgressItemDto unviewedDto = Assert.Single(result.Items, i => i.EntityId == unviewedMaterialId);
        Assert.Equal("NOT_VIEWED", unviewedDto.Status);
        Assert.Equal("Material", unviewedDto.EntityType);

        Assert.Contains(courseId, result.EnrolledCourseIds);
    }

    [Fact]
    public async Task GetRoadmapProgress_MaterialEntityType_IsAcceptedByValidator()
    {
        Guid userId = Guid.NewGuid();
        AuthenticateAs(userId, "platform-participant");

        var request = new GetRoadmapProgressRequest([
            new RoadmapProgressItemRequest("Material", Guid.NewGuid(), Guid.NewGuid()),
        ]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(ENDPOINT, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
