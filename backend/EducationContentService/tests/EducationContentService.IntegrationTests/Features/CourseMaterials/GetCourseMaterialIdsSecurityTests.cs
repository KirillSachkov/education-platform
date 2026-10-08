using System.Net;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;

namespace EducationContentService.IntegrationTests.Features.CourseMaterials;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetCourseMaterialIdsSecurityTests : EducationContentServiceTestsBase
{
    public GetCourseMaterialIdsSecurityTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Anonymous_IsRejected()
    {
        Guid courseId = await SeedCourseAsync(Guid.NewGuid());
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/materials/ids");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ForeignAuthor_IsForbidden()
    {
        Guid courseId = await SeedCourseAsync(Guid.NewGuid());
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/materials/ids");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CourseOwner_ReturnsIds()
    {
        Guid ownerId = Guid.NewGuid();
        Guid courseId = await SeedCourseAsync(ownerId);
        AuthenticateAs(ownerId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/materials/ids");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> SeedCourseAsync(Guid authorId)
    {
        Course? course = null;
        await ExecuteInDb(async db =>
        {
            string marker = Guid.NewGuid().ToString("N");
            course = new Course(
                authorId,
                Title.Create($"Course {marker}").Value,
                Description.Create("Description").Value,
                CourseSlug.Create($"course-{marker}").Value,
                SortKey.Initial());
            db.Courses.Add(course);
            await db.SaveChangesAsync();
        });

        return course!.Id;
    }
}
