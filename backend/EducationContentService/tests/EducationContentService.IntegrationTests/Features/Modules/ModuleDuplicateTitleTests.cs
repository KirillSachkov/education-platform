using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Modules;

/// <summary>
///     Regression coverage for the module-title uniqueness fix (#407): module titles are not
///     globally unique, so the same name can be reused across courses (and within a course).
///     Locks in the removal of the global <c>ix_modules_title</c> unique index.
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public class ModuleDuplicateTitleTests : EducationContentServiceTestsBase
{
    public ModuleDuplicateTitleTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task PublishModule_SameTitleInDifferentCourse_Succeeds()
    {
        CancellationToken ct = CancellationToken.None;
        const string sharedTitle = "Введение";

        Guid courseA = await CreateCourseInDb(ct);
        Guid moduleA = await CreateModuleViaApi(courseA, sharedTitle, ct);
        HttpResponseMessage publishA = await AppHttpClient.PostAsync($"/modules/{moduleA}/publish", content: null, ct);
        publishA.EnsureSuccessStatusCode();

        Guid courseB = await CreateCourseInDb(ct);
        Guid moduleB = await CreateModuleViaApi(courseB, sharedTitle, ct);

        HttpResponseMessage publishB = await AppHttpClient.PostAsync($"/modules/{moduleB}/publish", content: null, ct);

        Assert.Equal(HttpStatusCode.OK, publishB.StatusCode);
    }

    [Fact]
    public async Task PublishModule_SameTitleInSameCourse_Succeeds()
    {
        CancellationToken ct = CancellationToken.None;
        const string sharedTitle = "Практика";

        Guid courseId = await CreateCourseInDb(ct);

        Guid moduleA = await CreateModuleViaApi(courseId, sharedTitle, ct);
        HttpResponseMessage publishA = await AppHttpClient.PostAsync($"/modules/{moduleA}/publish", content: null, ct);
        publishA.EnsureSuccessStatusCode();

        Guid moduleB = await CreateModuleViaApi(courseId, sharedTitle, ct);

        HttpResponseMessage publishB = await AppHttpClient.PostAsync($"/modules/{moduleB}/publish", content: null, ct);

        Assert.Equal(HttpStatusCode.OK, publishB.StatusCode);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Guid> CreateCourseInDb(CancellationToken ct)
    {
        Guid courseId = Guid.Empty;
        string uniquePrefix = Guid.NewGuid().ToString("N")[..8];

        await ExecuteInDb(async db =>
        {
            var course = new Course(
                Guid.CreateVersion7(),
                Title.Create($"Course {uniquePrefix}").Value,
                Description.Create("Test Description").Value,
                CourseSlug.Create($"course-{uniquePrefix}").Value, SortKey.Initial());

            db.Courses.Add(course);
            courseId = course.Id;
            await db.SaveChangesAsync(ct);
        });

        return courseId;
    }

    private async Task<Guid> CreateModuleViaApi(Guid courseId, string title, CancellationToken ct)
    {
        var request = new CreateCourseModuleRequest(title, "Module Description");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/modules", request, ct);
        response.EnsureSuccessStatusCode();

        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>(ct);
        return envelope!.Result;
    }
}
