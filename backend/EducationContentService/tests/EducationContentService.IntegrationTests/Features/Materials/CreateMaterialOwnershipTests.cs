using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Materials;
using EducationContentService.IntegrationTests.Infrastructure;

namespace EducationContentService.IntegrationTests.Features.Materials;

/// <summary>
///     Cross-author ownership rejection coverage for <c>POST /materials</c> when
///     <c>CourseId</c> is supplied. Locks in the course ownership check introduced
///     in the ECS audit (issue #259) — Author A must not be able to create a
///     material under Author B's course.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class CreateMaterialOwnershipTests : EducationContentServiceTestsBase
{
    public CreateMaterialOwnershipTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateMaterial_WithCourseIdOwnedByAnotherAuthor_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();
        Guid courseBId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, authorB, "b-mat-create", ct));

        AuthenticateAs(authorA, "platform-author");
        var request = new CreateMaterialRequest(
            Title: "Hijacked material",
            Content: "Some content",
            Kind: "ARTICLE",
            AccessType: "PUBLIC",
            CourseId: courseBId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_WithCourseIdOwnedByCaller_Returns200()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();
        Guid courseId = await ExecuteInDb(db => OwnershipTestSeed.CreateCourseAsync(db, ownerId, "owner-mat-create", ct));

        AuthenticateAs(ownerId, "platform-author");
        var request = new CreateMaterialRequest(
            Title: "Owner material",
            Content: "Owner content",
            Kind: "ARTICLE",
            AccessType: "PUBLIC",
            CourseId: courseId);

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateMaterial_WithoutCourseId_BypassesCourseOwnershipCheck_Returns200()
    {
        // Orphan material (no CourseId) — there's no course to check ownership against,
        // so the endpoint succeeds regardless of which author calls it. Guard against
        // an over-broad check accidentally rejecting orphan creates.
        CancellationToken ct = CancellationToken.None;
        AuthenticateAs(Guid.NewGuid(), "platform-author");
        var request = new CreateMaterialRequest(
            Title: "Orphan material",
            Content: "Content",
            Kind: "ARTICLE",
            AccessType: "PUBLIC");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/materials", request, ct);

        response.EnsureSuccessStatusCode();
    }
}
