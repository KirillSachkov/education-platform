using System.Net;
using EducationContentService.IntegrationTests.Infrastructure;

namespace EducationContentService.IntegrationTests.Features.Courses;

/// <summary>
///     Cross-author ownership rejection coverage for the author-scoped course
///     read endpoints (<c>GetCourseDetail</c>, <c>GetCourseBuilder</c>) — both
///     return the editor DTO including draft content and must not leak across
///     authors. Locks in the checks introduced in the ECS audit (issue #259).
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class CourseQueryOwnershipTests : EducationContentServiceTestsBase
{
    public CourseQueryOwnershipTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ── GET /courses/{id}/detail ──────────────────────────────────────────

    [Fact]
    public async Task GetCourseDetail_AuthorAReadingAuthorBsCourse_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, authorB, "b-detail", ct));

        AuthenticateAs(authorA, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/detail", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCourseDetail_OwnerAuthor_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, ownerId, "owner-detail", ct));

        AuthenticateAs(ownerId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/detail", ct);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetCourseDetail_Admin_BypassesOwnership_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid foreignAuthor = Guid.NewGuid();
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, foreignAuthor, "admin-detail", ct));

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/detail", ct);

        response.EnsureSuccessStatusCode();
    }

    // ── GET /courses/{id}/builder ─────────────────────────────────────────

    [Fact]
    public async Task GetCourseBuilder_AuthorAReadingAuthorBsCourse_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, authorB, "b-builder", ct));

        AuthenticateAs(authorA, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/builder", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCourseBuilder_OwnerAuthor_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, ownerId, "owner-builder", ct));

        AuthenticateAs(ownerId, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync($"/courses/{courseId}/builder", ct);

        response.EnsureSuccessStatusCode();
    }
}
