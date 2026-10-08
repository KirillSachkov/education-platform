using System.Net;
using System.Net.Http.Json;
using ProgressService.Contracts.Requests;
using ProgressService.Contracts.Responses;
using ProgressService.Domain.Enrollments;
using ProgressService.Domain.Issues;
using ProgressService.Domain.Materials;
using ProgressService.Domain.Modules;
using ProgressService.Domain.Projects;
using ProgressService.IntegrationTests.Infrastructure;

namespace ProgressService.IntegrationTests.Features.RoadmapProgress;

[Collection(nameof(IntegrationTestsFixture))]
public class RoadmapProgressEndpointsTests : ProgressServiceTestsBase
{
    private const string ENDPOINT = "/progress/roadmap-progress";

    public RoadmapProgressEndpointsTests(IntegrationTestsWebFactory factory)
        : base(factory)
    {
    }

    [Fact]
    public async Task GetRoadmapProgress_WithoutAuth_ShouldReturn401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            ENDPOINT,
            new GetRoadmapProgressRequest([
                new RoadmapProgressItemRequest("Material", Guid.NewGuid(), null)
            ]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetRoadmapProgress_WithMoreThan200Items_ShouldReturnValidationError()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        List<RoadmapProgressItemRequest> items = Enumerable.Range(0, 201)
            .Select(_ => new RoadmapProgressItemRequest("Material", Guid.NewGuid(), null))
            .ToList();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            ENDPOINT,
            new GetRoadmapProgressRequest(items));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoadmapProgress_WithEmptyItems_ShouldReturnValidationError()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            ENDPOINT,
            new GetRoadmapProgressRequest([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetRoadmapProgress_WithMixedEntityTypes_ReturnsCorrectStatuses()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid moduleId = Guid.NewGuid();
        Guid projectId = Guid.NewGuid();
        Guid viewedLessonId = Guid.NewGuid();
        Guid unviewedLessonId = Guid.NewGuid();
        Guid approvedIssueId = Guid.NewGuid();
        Guid unseenIssueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        await ExecuteInDb(async db =>
        {
            CourseEnrollment enrollment = CourseEnrollment.CreateAnchor(userId, courseId, Guid.NewGuid(), EnrollmentSource.ENGAGEMENT).Value;
            db.CourseEnrollments.Add(enrollment);

            // User-scoped MaterialView: просмотр живёт вне enrollment'а.
            MaterialView view = MaterialView.Create(userId, viewedLessonId).Value;
            db.MaterialViews.Add(view);

            IssueProgress issue = IssueProgress.Create(enrollment.Id, projectId, approvedIssueId).Value;
            issue.StartWork();
            issue.SubmitForReview();
            issue.Approve();
            db.IssueProgresses.Add(issue);

            ModuleProgress module = ModuleProgress.Create(enrollment.Id, moduleId, itemsTotal: 2).Value;
            db.ModuleProgresses.Add(module);

            ProjectProgress project = ProjectProgress.Create(enrollment.Id, projectId, totalIssuesCount: 2).Value;
            db.ProjectProgresses.Add(project);

            await db.SaveChangesAsync();
        });

        var request = new GetRoadmapProgressRequest([
            new RoadmapProgressItemRequest("Material", viewedLessonId, courseId),
            new RoadmapProgressItemRequest("Material", unviewedLessonId, courseId),
            new RoadmapProgressItemRequest("Issue", approvedIssueId, null),
            new RoadmapProgressItemRequest("Issue", unseenIssueId, courseId),
            new RoadmapProgressItemRequest("Module", moduleId, null),
            new RoadmapProgressItemRequest("Project", projectId, null),
        ]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(ENDPOINT, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetRoadmapProgressResponse result =
            await ReadWrappedResultAsync<GetRoadmapProgressResponse>(response);

        RoadmapProgressItemDto viewedLessonDto = Assert.Single(result.Items,
            i => i.EntityId == viewedLessonId);
        Assert.Equal("VIEWED", viewedLessonDto.Status);
        Assert.Equal(courseId, viewedLessonDto.CourseId);

        RoadmapProgressItemDto unviewedLessonDto = Assert.Single(result.Items,
            i => i.EntityId == unviewedLessonId);
        Assert.Equal("NOT_VIEWED", unviewedLessonDto.Status);

        RoadmapProgressItemDto approvedIssueDto = Assert.Single(result.Items,
            i => i.EntityId == approvedIssueId);
        Assert.Equal("COMPLETED", approvedIssueDto.Status);

        RoadmapProgressItemDto unseenIssueDto = Assert.Single(result.Items,
            i => i.EntityId == unseenIssueId);
        Assert.Equal("NOT_STARTED", unseenIssueDto.Status);

        Assert.Contains(result.Items, i => i.EntityType == "Module" && i.EntityId == moduleId);
        Assert.Contains(result.Items, i => i.EntityType == "Project" && i.EntityId == projectId);

        // enrolledCourseIds includes the active enrollment
        Assert.Contains(courseId, result.EnrolledCourseIds);
    }

    [Fact]
    public async Task GetRoadmapProgress_UserWithNoEnrollments_ReturnsDefaultStatuses()
    {
        Guid userId = Guid.NewGuid();
        Guid courseId = Guid.NewGuid();
        Guid lessonId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        AuthenticateAs(userId, "platform-participant");

        var request = new GetRoadmapProgressRequest([
            new RoadmapProgressItemRequest("Material", lessonId, courseId),
            new RoadmapProgressItemRequest("Issue", issueId, courseId),
        ]);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(ENDPOINT, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        GetRoadmapProgressResponse result =
            await ReadWrappedResultAsync<GetRoadmapProgressResponse>(response);

        Assert.Equal(2, result.Items.Count);
        RoadmapProgressItemDto lessonDto = Assert.Single(result.Items, i => i.EntityId == lessonId);
        Assert.Equal("NOT_VIEWED", lessonDto.Status);

        RoadmapProgressItemDto issueDto = Assert.Single(result.Items, i => i.EntityId == issueId);
        Assert.Equal("NOT_STARTED", issueDto.Status);

        Assert.Empty(result.EnrolledCourseIds);
    }
}
