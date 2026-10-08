using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Courses;
using EducationContentService.IntegrationTests.Infrastructure;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Courses;

/// <summary>
///     Authorization coverage for <c>GET /courses/admin-list</c> (<c>GetAllCoursesAdmin</c>).
///     The endpoint lists courses across ALL authors (DRAFT / PUBLISHED / ARCHIVED) and is
///     consumed only by the admin MCP server via a service token. It must be admin/service-only:
///     a <c>platform-author</c> (who still holds <c>Courses.MANAGE</c> for their own content)
///     reaching it was a cross-author IDOR. Locks in the tightening from
///     <c>RequirePermissions(Courses.MANAGE)</c> to <c>RequireAnyRole(ADMIN, SERVICE)</c>.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetAllCoursesAdminTests : EducationContentServiceTestsBase
{
    public GetAllCoursesAdminTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AdminList_Anonymous_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/admin-list");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, got {response.StatusCode}");
    }

    [Fact]
    public async Task AdminList_NonAdminAuthor_Returns403()
    {
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/admin-list");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminList_Admin_Returns200_WithCoursesAcrossAuthors()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid courseA = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, authorA, "admin-a", ct));
        Guid courseB = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, authorB, "admin-b", ct));

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/admin-list", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<CourseSummaryDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<CourseSummaryDto>>>(ct);
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        // Cross-author visibility is the whole point of the admin-list endpoint.
        Assert.Contains(envelope.Result, c => c.Id == courseA);
        Assert.Contains(envelope.Result, c => c.Id == courseB);
    }
}
