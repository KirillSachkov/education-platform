using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class CatalogTests : EducationContentServiceTestsBase
{
    public CatalogTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ── GET /courses/catalog ──────────────────────────────────────────────

    [Fact]
    public async Task GetCatalog_ReturnsOnlyPublishedCourses()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        await CreateCourseInDb("Draft Course", "Draft desc", publish: false, ct);
        await CreateCourseInDb("Published Course", "Published desc", publish: true, ct);
        await CreateCourseInDb("Another Draft", "Another desc", publish: false, ct);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/courses/catalog?limit=10", ct);

        // Assert
        response.EnsureSuccessStatusCode();

        CursorResponse<CourseCatalogDto> result = await ReadResultAsync<CursorResponse<CourseCatalogDto>>(response);

        Assert.Single(result.Items);
        Assert.Equal("Published Course", result.Items[0].Title);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task GetCatalog_EmptyCatalog_ReturnsEmptyList()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/courses/catalog?limit=10", ct);

        // Assert
        response.EnsureSuccessStatusCode();

        CursorResponse<CourseCatalogDto> result = await ReadResultAsync<CursorResponse<CourseCatalogDto>>(response);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task GetCatalog_WithSearchFilter_ReturnsMatchingCourses()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        await CreateCourseInDb("C# Fundamentals", "Learn C# basics", publish: true, ct);
        await CreateCourseInDb("Python Basics", "Learn Python", publish: true, ct);
        await CreateCourseInDb("Advanced C#", "Deep dive into C#", publish: true, ct);

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/courses/catalog?limit=10&search=C%23", ct);

        // Assert
        response.EnsureSuccessStatusCode();

        CursorResponse<CourseCatalogDto> result = await ReadResultAsync<CursorResponse<CourseCatalogDto>>(response);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item =>
            Assert.Contains("C#", item.Title, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCatalog_WithPagination_ReturnsPaginatedResults()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        for (int i = 1; i <= 5; i++)
        {
            await CreateCourseInDb($"Course {i}", $"Description {i}", publish: true, ct);
            await Task.Delay(10, ct); // ensure different created_at timestamps
        }

        // Act — first page
        HttpResponseMessage firstResponse = await AppHttpClient.GetAsync(
            "/courses/catalog?limit=3", ct);
        firstResponse.EnsureSuccessStatusCode();

        CursorResponse<CourseCatalogDto> firstResult = await ReadResultAsync<CursorResponse<CourseCatalogDto>>(firstResponse);

        Assert.Equal(3, firstResult.Items.Count);
        Assert.NotNull(firstResult.NextCursor);
        Assert.Equal(5, firstResult.TotalCount);

        // Act — second page
        HttpResponseMessage secondResponse = await AppHttpClient.GetAsync(
            $"/courses/catalog?limit=3&cursor={firstResult.NextCursor}", ct);
        secondResponse.EnsureSuccessStatusCode();

        CursorResponse<CourseCatalogDto> secondResult = await ReadResultAsync<CursorResponse<CourseCatalogDto>>(secondResponse);

        Assert.Equal(2, secondResult.Items.Count);
        Assert.Null(secondResult.NextCursor);

        // Verify no overlap between pages
        List<Guid> firstPageIds = firstResult.Items.Select(c => c.Id).ToList();
        List<Guid> secondPageIds = secondResult.Items.Select(c => c.Id).ToList();
        Assert.Empty(firstPageIds.Intersect(secondPageIds));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Guid> CreateCourseInDb(
        string title, string description, bool publish, CancellationToken ct)
    {
        Guid courseId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            string slug = $"test-{Guid.NewGuid().ToString("N")[..8]}";
            var course = new Course(
                Guid.CreateVersion7(),
                Title.Create(title).Value,
                Description.Create(description).Value,
                CourseSlug.Create(slug).Value, SortKey.Initial());

            if (publish)
                course.Publish();

            db.Courses.Add(course);
            courseId = course.Id;
            await db.SaveChangesAsync(ct);
        });

        return courseId;
    }
}
