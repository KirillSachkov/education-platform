using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using SharedKernel;
using Ordering;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public class CourseTests : EducationContentServiceTestsBase
{
    public CourseTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    // ── POST /courses ──────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCourse_ValidRequest_CourseCreatedWithCorrectData()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        var request = new CreateCourseRequest("C# Fundamentals", "Learn C# from scratch", "csharp-fundamentals");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/courses", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Course? course = await db.Courses.FirstOrDefaultAsync(ct);
            Assert.NotNull(course);
            Assert.Equal("C# Fundamentals", course.Title.Value);
            Assert.Equal("Learn C# from scratch", course.Description.Value);
        });
    }

    [Fact]
    public async Task CreateCourse_EmptyTitle_ReturnsBadRequest()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        var request = new CreateCourseRequest("", "Valid description", "empty-title");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/courses", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCourse_TitleExceedsMaxLength_ReturnsBadRequest()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        string longTitle = new('A', Title.MAX_LENGTH + 1);
        var request = new CreateCourseRequest(longTitle, "Valid description", "long-title");

        // Act
        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/courses", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── PATCH /courses/{courseId} ──────────────────────────────────────────

    [Fact]
    public async Task UpdateCourse_ValidRequest_UpdatesTitleAndDescription()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb("Original Title", "Original Desc", ct);

        var request = new UpdateCourseRequest("Updated Title", "Updated Desc");

        // Act
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}", request, ct);

        // Assert
        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Course? course = await db.Courses.FirstAsync(c => c.Id == courseId, ct);
            Assert.Equal("Updated Title", course.Title.Value);
            Assert.Equal("Updated Desc", course.Description.Value);
        });
    }

    [Fact]
    public async Task UpdateCourse_NonExistentCourse_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        var request = new UpdateCourseRequest("Title", "Description");

        // Act
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{Guid.NewGuid()}", request, ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCourse_PartialUpdate_DoesNotWipeUntouchedFields()
    {
        // Регрессия: PATCH без gettingStartedModuleId не должен зануливать это поле.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb("Original Title", "Original Desc", ct);

        // Шлём только title и description — все остальные поля отсутствуют в JSON.
        var request = new UpdateCourseRequest(
            Title: "Renamed",
            Description: "Renamed Desc");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Course course = await db.Courses.FirstAsync(c => c.Id == courseId, ct);
            Assert.Equal("Renamed", course.Title.Value);
            Assert.Equal("Renamed Desc", course.Description.Value);
        });
    }

    [Fact]
    public async Task UpdateCourse_ChangeKind_IssuelessCourse_UpdatesKind()
    {
        // Корректировка типа продукта (#640): курс без заданий → марафон.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb("Project Marathon", "desc", ct);

        var request = new UpdateCourseRequest(Kind: "MARATHON");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}", request, ct);

        response.EnsureSuccessStatusCode();
        await ExecuteInDb(async db =>
        {
            Course course = await db.Courses.FirstAsync(c => c.Id == courseId, ct);
            Assert.Equal(CourseKind.MARATHON, course.Kind);
        });
    }

    [Fact]
    public async Task UpdateCourse_InvalidKind_ReturnsBadRequest()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb("Course", "desc", ct);

        var request = new UpdateCourseRequest(Kind: "NONSENSE");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}", request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCourse_ChangeKindToIntensive_CourseHasIssues_ReturnsBadRequest()
    {
        // Guard #640: курс с заданием в модуле нельзя сделать интенсивом/марафоном.
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb("Course With Issues", "desc", ct);

        // Сеем issue-item напрямую (минуя AttachIssueToModule) — проверяем именно DB-read
        // гарда HasIssueItemsInCourseAsync, а не интеграцию с attach-use-case'ом.
        await ExecuteInDb(async db =>
        {
            Guid authorId = Guid.NewGuid();
            Guid moduleId = await OwnershipTestSeed.CreateModuleAsync(db, authorId, "kind", ct: ct);
            await OwnershipTestSeed.AttachItemToCourseAsync(db, courseId, CourseItemType.Module, moduleId, ct);
            Guid projectId = await OwnershipTestSeed.CreateProjectAsync(db, authorId, "kind", ct);
            Guid issueId = await OwnershipTestSeed.CreateIssueAsync(db, authorId, projectId, "kind", ct);
            db.ModuleItems.Add(new ModuleItem(
                moduleId, ModuleItemType.Issue, issueId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
        });

        var request = new UpdateCourseRequest(Kind: "INTENSIVE");

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}", request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("course.kind.has.issues", body, StringComparison.Ordinal);

        await ExecuteInDb(async db =>
        {
            Course course = await db.Courses.FirstAsync(c => c.Id == courseId, ct);
            Assert.Equal(CourseKind.COURSE, course.Kind);
        });
    }

    // ── GET /courses/{courseId}/detail ─────────────────────────────────────

    [Fact]
    public async Task GetDetail_ExistingCourseWithModuleAndProject_ReturnsCourseWithItems()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb("Detail Course", "Detail Desc", ct);

        // Add a module and a project via API
        var moduleRequest = new CreateCourseModuleRequest("Module 1", "Module Description");
        HttpResponseMessage moduleResponse = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/modules", moduleRequest, ct);
        moduleResponse.EnsureSuccessStatusCode();

        var projectRequest = new CreateCourseProjectRequest("Project 1", "Project Description");
        HttpResponseMessage projectResponse = await AppHttpClient.PostAsJsonAsync(
            $"/courses/{courseId}/projects", projectRequest, ct);
        projectResponse.EnsureSuccessStatusCode();

        // Act
        HttpResponseMessage detailResponse = await AppHttpClient.GetAsync(
            $"/courses/{courseId}/detail", ct);

        // Assert
        detailResponse.EnsureSuccessStatusCode();

        Envelope<CourseDetailDto>? envelope =
            await detailResponse.Content.ReadFromJsonAsync<Envelope<CourseDetailDto>>(ct);
        Assert.NotNull(envelope);
        CourseDetailDto dto = envelope.Result!;
        Assert.Equal(courseId, dto.Id);
        Assert.Equal("Detail Course", dto.Title);
        Assert.Equal("Detail Desc", dto.Description);
        Assert.Equal("DRAFT", dto.Status);
        Assert.Equal(2, dto.Items.Count);

        // First item = module (added first), second = project
        Assert.Equal("Module", dto.Items[0].ItemType);
        Assert.Equal("Module 1", dto.Items[0].Title);
        Assert.Equal("Project", dto.Items[1].ItemType);
        Assert.Equal("Project 1", dto.Items[1].Title);
    }

    [Fact]
    public async Task GetDetail_NonExistentCourse_ReturnsNotFound()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/courses/{Guid.NewGuid()}/detail", ct);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_ItemsReturnedInSortKeyOrder()
    {
        // Arrange
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseInDb("Ordered Course", "Ordered Desc", ct);

        // Add 3 modules sequentially — they get sequential sort keys
        for (int i = 1; i <= 3; i++)
        {
            var request = new CreateCourseModuleRequest($"Module {i}", $"Desc {i}");
            HttpResponseMessage r = await AppHttpClient.PostAsJsonAsync(
                $"/courses/{courseId}/modules", request, ct);
            r.EnsureSuccessStatusCode();
        }

        // Act
        HttpResponseMessage detailResponse = await AppHttpClient.GetAsync(
            $"/courses/{courseId}/detail", ct);

        // Assert
        detailResponse.EnsureSuccessStatusCode();
        Envelope<CourseDetailDto>? envelope =
            await detailResponse.Content.ReadFromJsonAsync<Envelope<CourseDetailDto>>(ct);
        Assert.NotNull(envelope);
        CourseDetailDto dto = envelope.Result!;
        Assert.Equal(3, dto.Items.Count);
        Assert.Equal("Module 1", dto.Items[0].Title);
        Assert.Equal("Module 2", dto.Items[1].Title);
        Assert.Equal("Module 3", dto.Items[2].Title);

        // Sort keys should be strictly ascending
        for (int i = 1; i < dto.Items.Count; i++)
        {
            Assert.True(
                string.Compare(dto.Items[i - 1].SortKey, dto.Items[i].SortKey, StringComparison.Ordinal) < 0,
                $"Sort key {dto.Items[i - 1].SortKey} should be less than {dto.Items[i].SortKey}");
        }
    }

    // ── GET /courses/my — auth, permission, ownership isolation ────────────

    [Fact]
    public async Task GetMyCourses_Anonymous_ShouldReturn401()
    {
        CancellationToken ct = CancellationToken.None;
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/my?limit=10", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMyCourses_Participant_ShouldReturn403()
    {
        // platform-participant has no content.manage permission → 403
        CancellationToken ct = CancellationToken.None;
        AuthenticateAs(Guid.NewGuid(), "platform-participant");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/my?limit=10", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetMyCourses_AuthorA_ShouldNotSeeAuthorBCourses()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorA = Guid.NewGuid();
        Guid authorB = Guid.NewGuid();

        await ExecuteInDb(async db =>
        {
            var courseA = new Course(
                authorA,
                Title.Create("Author A Course").Value,
                Description.Create("A").Value,
                CourseSlug.Create("author-a-course").Value, SortKey.Initial());
            var courseB = new Course(
                authorB,
                Title.Create("Author B Course").Value,
                Description.Create("B").Value,
                CourseSlug.Create("author-b-course").Value, SortKey.Initial());
            db.Courses.AddRange(courseA, courseB);
            await db.SaveChangesAsync(ct);
        });

        AuthenticateAs(authorA, "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/my?limit=10", ct);
        response.EnsureSuccessStatusCode();

        CursorResponse<CourseSummaryDto> result =
            await ReadResultAsync<CursorResponse<CourseSummaryDto>>(response);

        Assert.Single(result.Items);
        Assert.Equal("Author A Course", result.Items[0].Title);
        Assert.All(result.Items, c => Assert.Equal(authorA, c.AuthorId));
    }

    [Fact]
    public async Task GetMyCourses_Empty_ShouldReturnEmptyList()
    {
        CancellationToken ct = CancellationToken.None;
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/my?limit=10", ct);
        response.EnsureSuccessStatusCode();

        CursorResponse<CourseSummaryDto> result =
            await ReadResultAsync<CursorResponse<CourseSummaryDto>>(response);

        Assert.Empty(result.Items);
        Assert.Null(result.NextCursor);
        Assert.Equal(0, result.TotalCount);
    }

    // ── GET /courses/my — limit clamping ───────────────────────────────────

    [Fact]
    public async Task GetMyCourses_WithLimit10000_ClampsToMax100()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAs(authorId, "platform-admin");

        await ExecuteInDb(async db =>
        {
            for (int i = 0; i < 105; i++)
            {
                var course = new Course(
                    authorId,
                    Title.Create($"Course {i:D3}").Value,
                    Description.Create("Description").Value,
                    CourseSlug.Create($"course-{i:D3}").Value, SortKey.Initial());
                db.Courses.Add(course);
            }
            await db.SaveChangesAsync(ct);
        });

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/my?limit=10000", ct);

        response.EnsureSuccessStatusCode();

        Envelope<CursorResponse<CourseSummaryDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CursorResponse<CourseSummaryDto>>>(ct);
        Assert.NotNull(envelope);
        CursorResponse<CourseSummaryDto> result = envelope.Result!;

        // Limit is clamped to 100 — must return at most 100 items.
        Assert.True(result.Items.Count <= 100, $"Expected ≤100 items, got {result.Items.Count}");
    }

    // ── PATCH /courses/{id}/move — drag-n-drop ordering ───────────────────

    [Fact]
    public async Task MoveCourse_BetweenTwoCourses_UpdatesOrder()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAs(authorId, "platform-author");

        Guid first = await CreateCourseForAuthor(authorId, "Alpha", "alpha", ct);
        Guid second = await CreateCourseForAuthor(authorId, "Bravo", "bravo", ct);
        Guid third = await CreateCourseForAuthor(authorId, "Charlie", "charlie", ct);

        // Read current sort keys.
        (string firstKey, string thirdKey) = await ReadSortKeys(first, third);

        // Move `third` between `first` and `second`.
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{third}/move",
            new MoveCourseRequest(AfterSortKey: firstKey, BeforeSortKey: await ReadSortKey(second)),
            ct);

        response.EnsureSuccessStatusCode();

        HttpResponseMessage list = await AppHttpClient.GetAsync("/courses/my?limit=10", ct);
        list.EnsureSuccessStatusCode();
        CursorResponse<CourseSummaryDto> result =
            await ReadResultAsync<CursorResponse<CourseSummaryDto>>(list);

        // Expected order after move: Alpha, Charlie, Bravo
        Assert.Equal(["Alpha", "Charlie", "Bravo"], result.Items.Select(x => x.Title).ToArray());
    }

    [Fact]
    public async Task MoveCourse_NotOwner_Returns403()
    {
        CancellationToken ct = CancellationToken.None;
        Guid ownerId = Guid.NewGuid();
        Guid otherId = Guid.NewGuid();

        Guid courseId = await CreateCourseForAuthor(ownerId, "Owner course", "owner", ct);

        AuthenticateAs(otherId, "platform-author");
        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}/move",
            new MoveCourseRequest(AfterSortKey: "a0", BeforeSortKey: null),
            ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MoveCourse_BothBoundsNull_Returns400()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAs(authorId, "platform-author");
        Guid courseId = await CreateCourseForAuthor(authorId, "X", "x", ct);

        HttpResponseMessage response = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{courseId}/move",
            new MoveCourseRequest(AfterSortKey: null, BeforeSortKey: null),
            ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMyCourses_OrdersBySortKey_NotByCreatedAt()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAs(authorId, "platform-author");

        // Create A, B, C — created in this order, so naïve created_at sort would yield A, B, C.
        // Then move A to the end. With sort_key sort, the order should be B, C, A.
        Guid a = await CreateCourseForAuthor(authorId, "A", "a-course", ct);
        Guid b = await CreateCourseForAuthor(authorId, "B", "b-course", ct);
        Guid c = await CreateCourseForAuthor(authorId, "C", "c-course", ct);

        string cKey = await ReadSortKey(c);
        HttpResponseMessage move = await AppHttpClient.PatchAsJsonAsync(
            $"/courses/{a}/move",
            new MoveCourseRequest(AfterSortKey: cKey, BeforeSortKey: null),
            ct);
        move.EnsureSuccessStatusCode();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/my?limit=10", ct);
        response.EnsureSuccessStatusCode();
        CursorResponse<CourseSummaryDto> result =
            await ReadResultAsync<CursorResponse<CourseSummaryDto>>(response);

        Assert.Equal(["B", "C", "A"], result.Items.Select(x => x.Title).ToArray());
        Assert.Equal([b, c, a], result.Items.Select(x => x.Id).ToArray());
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Guid> CreateCourseInDb(
        string title,
        string description,
        CancellationToken ct)
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

            db.Courses.Add(course);
            courseId = course.Id;
            await db.SaveChangesAsync(ct);
        });

        return courseId;
    }

    private async Task<Guid> CreateCourseForAuthor(
        Guid authorId, string title, string slug, CancellationToken ct)
    {
        Guid? courseId = null;
        await ExecuteInDb(async db =>
        {
            // Compute next sort key per author from current state.
            string? lastKey = await db.Courses
                .Where(x => x.AuthorId == authorId)
                .OrderByDescending(x => x.SortKey)
                .Select(x => x.SortKey.Value)
                .FirstOrDefaultAsync(ct);

            SortKey nextKey = lastKey is null
                ? SortKey.Initial()
                : SortKey.After(SortKey.Create(lastKey).Value);

            var course = new Course(
                authorId,
                Title.Create(title).Value,
                Description.Create($"{title} description").Value,
                CourseSlug.Create($"{slug}-{Guid.NewGuid().ToString("N")[..6]}").Value,
                nextKey);
            db.Courses.Add(course);
            await db.SaveChangesAsync(ct);
            courseId = course.Id;
        });
        return courseId!.Value;
    }

    private async Task<string> ReadSortKey(Guid courseId)
    {
        string? key = null;
        await ExecuteInDb(async db =>
        {
            key = await db.Courses
                .Where(c => c.Id == courseId)
                .Select(c => c.SortKey.Value)
                .FirstOrDefaultAsync();
        });
        return key!;
    }

    private async Task<(string, string)> ReadSortKeys(Guid a, Guid b)
        => (await ReadSortKey(a), await ReadSortKey(b));
}
