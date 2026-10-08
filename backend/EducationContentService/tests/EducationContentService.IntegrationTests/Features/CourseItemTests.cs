using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class CourseItemTests : EducationContentServiceTestsBase
{
    public CourseItemTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ── 401 Unauthorized ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateModule_WithoutAuth_ShouldReturn401()
    {
        // Arrange
        RemoveAuthentication();
        var request = new CreateCourseModuleRequest("Module Title", "Module Description");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{Guid.NewGuid()}/modules", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── POST /courses/{courseId}/modules ───────────────────────────────────

    [Fact]
    public async Task CreateModule_ValidRequest_CreatesModuleAndCourseItem()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        var request = new CreateCourseModuleRequest("Module Title", "Module Description");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/modules", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Module? module = await db.Modules.FirstOrDefaultAsync(ct);
            Assert.NotNull(module);
            Assert.Equal("Module Title", module.Title.Value);
            Assert.Equal("Module Description", module.Description?.Value);

            CourseItem? item = await db.CourseItems
                .FirstOrDefaultAsync(i => i.CourseId == courseId && i.ReferenceId == module.Id, ct);
            Assert.NotNull(item);
            Assert.Equal(CourseItemType.Module, item.ItemType);
            Assert.False(item.IsOptional);
        });
    }

    [Fact]
    public async Task CreateModule_NonExistentCourse_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        var request = new CreateCourseModuleRequest("Module", "Description");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{Guid.NewGuid()}/modules", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateModule_MultipleModules_EachGetsUniqueSortKey()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);

        // Act — create 3 modules in sequence
        for (int i = 1; i <= 3; i++)
        {
            var request = new CreateCourseModuleRequest($"Module {i}", $"Description {i}");
            HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
                $"/courses/{courseId}/modules", request, ct);
            response.EnsureSuccessStatusCode();
        }

        // Assert — all sort keys are unique and ordered
        await ExecuteInDb(async db =>
        {
            List<CourseItem> items = await db.CourseItems
                .Where(i => i.CourseId == courseId)
                .OrderBy(i => i.SortKey)
                .ToListAsync(ct);

            Assert.Equal(3, items.Count);

            List<string> sortKeys = items.Select(i => i.SortKey.Value).ToList();
            Assert.Equal(sortKeys.Count, sortKeys.Distinct().Count());

            // Each subsequent sort key should be greater
            for (int i = 1; i < sortKeys.Count; i++)
            {
                Assert.True(
                    string.Compare(sortKeys[i - 1], sortKeys[i], StringComparison.Ordinal) < 0);
            }
        });
    }

    // ── POST /courses/{courseId}/projects ──────────────────────────────────

    [Fact]
    public async Task CreateProject_ValidRequest_CreatesProjectAndCourseItem()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        var request = new CreateCourseProjectRequest("Project Title", "Project Description");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/projects", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Project? project = await db.Projects.FirstOrDefaultAsync(ct);
            Assert.NotNull(project);
            Assert.Equal("Project Title", project.Title.Value);
            Assert.Equal("Project Description", project.Description!.Value);

            CourseItem? item = await db.CourseItems
                .FirstOrDefaultAsync(i => i.CourseId == courseId && i.ReferenceId == project.Id, ct);
            Assert.NotNull(item);
            Assert.Equal(CourseItemType.Project, item.ItemType);

            ProjectReviewContext? context = await db.ProjectReviewContexts
                .FirstOrDefaultAsync(x => x.ProjectId == project.Id, ct);
            Assert.NotNull(context);
            Assert.True(context.RequiresGithubConnection);
            Assert.True(context.RequiresReviewApp);
            Assert.True(context.IsAutoReviewEnabled);
        });
    }

    [Fact]
    public async Task CreateProject_CustomReviewSettings_PersistsReviewContext()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        var request = new CreateCourseProjectRequest(
            "Self Check Project",
            "Project Description",
            DetailedDescription: null,
            RequiresGithubConnection: false,
            RequiresReviewApp: false,
            IsAutoReviewEnabled: false);

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/projects", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();
        Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>(ct);
        Guid projectId = envelope!.Result;

        await ExecuteInDb(async db =>
        {
            ProjectReviewContext context = await db.ProjectReviewContexts
                .SingleAsync(x => x.ProjectId == projectId, ct);

            Assert.False(context.RequiresGithubConnection);
            Assert.False(context.RequiresReviewApp);
            Assert.False(context.IsAutoReviewEnabled);
        });
    }

    // ── PATCH /courses/{courseId}/items/{referenceId}/move ─────────────────

    [Fact]
    public async Task MoveItem_MoveToFirst_PositionChanges()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid[] moduleIds = await CreateCourseModulesViaApi(courseId, 3, ct);

        (Guid ReferenceId, string SortKey)[] items = await GetCourseItemsOrdered(courseId, ct);

        // Act — move last item to first position
        var request = new MoveCourseItemRequest(AfterSortKey: null, BeforeSortKey: items[0].SortKey);
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}/items/{items[2].ReferenceId}/move", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        (Guid ReferenceId, string SortKey)[] newItems = await GetCourseItemsOrdered(courseId, ct);
        Assert.Equal(items[2].ReferenceId, newItems[0].ReferenceId);
        Assert.Equal(items[0].ReferenceId, newItems[1].ReferenceId);
        Assert.Equal(items[1].ReferenceId, newItems[2].ReferenceId);
    }

    [Fact]
    public async Task MoveItem_BothSortKeysNull_ReturnsBadRequest()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid[] moduleIds = await CreateCourseModulesViaApi(courseId, 2, ct);
        (Guid ReferenceId, string SortKey)[] items = await GetCourseItemsOrdered(courseId, ct);

        // Act
        var request = new MoveCourseItemRequest(AfterSortKey: null, BeforeSortKey: null);
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}/items/{items[0].ReferenceId}/move", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MoveItem_ItemNotInCourse_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId1 = await CreateCourseInDb(ct);
        Guid courseId2 = await CreateCourseInDb(ct);
        Guid[] modulesC1 = await CreateCourseModulesViaApi(courseId1, 1, ct);
        Guid[] modulesC2 = await CreateCourseModulesViaApi(courseId2, 1, ct);
        (Guid ReferenceId, string SortKey)[] itemsC2 = await GetCourseItemsOrdered(courseId2, ct);

        // Act — try to move module from course1 under course2's endpoint
        var request = new MoveCourseItemRequest(AfterSortKey: itemsC2[0].SortKey, BeforeSortKey: null);
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId2}/items/{modulesC1[0]}/move", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── DELETE /courses/{courseId}/items/{referenceId} ─────────────────────

    [Fact]
    public async Task DetachItem_ExistingItem_RemovesCourseItemAndKeepsModule()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);
        Guid[] moduleIds = await CreateCourseModulesViaApi(courseId, 1, ct);

        // Act
        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/courses/{courseId}/items/{moduleIds[0]}", ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            // CourseItem should be removed
            bool itemExists = await db.CourseItems
                .AnyAsync(i => i.CourseId == courseId && i.ReferenceId == moduleIds[0], ct);
            Assert.False(itemExists);

            // Module aggregate should still exist
            bool moduleExists = await db.Modules.AnyAsync(m => m.Id == moduleIds[0], ct);
            Assert.True(moduleExists);
        });
    }

    [Fact]
    public async Task DetachItem_NonExistentItem_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb(ct);

        // Act
        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/courses/{courseId}/items/{Guid.NewGuid()}", ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DetachItem_CrossCourseItem_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId1 = await CreateCourseInDb(ct);
        Guid courseId2 = await CreateCourseInDb(ct);
        Guid[] modulesC1 = await CreateCourseModulesViaApi(courseId1, 1, ct);

        // Act — try to detach module from course1 using course2's endpoint
        HttpResponseMessage response = await AppHttpClient.DeleteAsync(
            $"/courses/{courseId2}/items/{modulesC1[0]}", ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    /// <summary>Creates modules via the API and returns their IDs.</summary>
    private async Task<Guid[]> CreateCourseModulesViaApi(Guid courseId, int count, CancellationToken ct)
    {
        string uniquePrefix = Guid.NewGuid().ToString("N")[..8];
        List<Guid> moduleIds = [];

        for (int i = 0; i < count; i++)
        {
            var request = new CreateCourseModuleRequest(
                $"Module {uniquePrefix} {i + 1}",
                $"Module Description {i + 1}");

            HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
                $"/courses/{courseId}/modules", request, ct);
            response.EnsureSuccessStatusCode();

            Envelope<Guid>? envelope = await response.Content.ReadFromJsonAsync<Envelope<Guid>>(ct);
            moduleIds.Add(envelope!.Result);
        }

        return moduleIds.ToArray();
    }

    private async Task<(Guid ReferenceId, string SortKey)[]> GetCourseItemsOrdered(
        Guid courseId, CancellationToken ct)
    {
        CourseItem[] items = [];

        await ExecuteInDb(async db =>
        {
            items = await db.CourseItems
                .Where(i => i.CourseId == courseId)
                .OrderBy(i => i.SortKey)
                .ToArrayAsync(ct);
        });

        return items.Select(i => (i.ReferenceId, i.SortKey.Value)).ToArray();
    }
}
