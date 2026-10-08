using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts;
using EducationContentService.Contracts.Courses;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.Courses;

/// <summary>
///     Тесты дискриминатора <see cref="CourseKind"/> (issue #291 — INTENSIVE, issue #374 — MARATHON):
///     <list type="bullet">
///         <item>POST /courses принимает kind и сохраняет его в БД (default — COURSE).</item>
///         <item>GET /courses/catalog?kind=INTENSIVE|MARATHON фильтрует только курсы этого типа.</item>
///         <item>POST /modules/{moduleId}/issues/{issueId} → 400, если модуль принадлежит интенсиву/марафону.</item>
///     </list>
/// </summary>
[Collection(nameof(IntegrationTestsFixture))]
public sealed class CourseKindTests : EducationContentServiceTestsBase
{
    public CourseKindTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task CreateCourse_WithKindIntensive_StoresIntensive()
    {
        CancellationToken ct = CancellationToken.None;
        var request = new CreateCourseRequest(
            "Intensive Course", "Quick deep dive", "intensive-1", Kind: "INTENSIVE");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/courses", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Course course = await db.Courses.SingleAsync(ct);
            Assert.Equal(CourseKind.INTENSIVE, course.Kind);
        });
    }

    [Fact]
    public async Task CreateCourse_WithoutKind_DefaultsToCourse()
    {
        CancellationToken ct = CancellationToken.None;
        var request = new CreateCourseRequest(
            "Regular Course", "Full course", "course-1");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/courses", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Course course = await db.Courses.SingleAsync(ct);
            Assert.Equal(CourseKind.COURSE, course.Kind);
        });
    }

    [Fact]
    public async Task CreateCourse_WithInvalidKind_ReturnsBadRequest()
    {
        CancellationToken ct = CancellationToken.None;
        var request = new CreateCourseRequest(
            "Bad Course", "Desc", "bad-1", Kind: "WORKSHOP");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/courses", request, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCatalog_WithKindFilter_ReturnsOnlyMatchingKind()
    {
        CancellationToken ct = CancellationToken.None;
        await CreatePublishedCourseInDb("Course A", CourseKind.COURSE, ct);
        await CreatePublishedCourseInDb("Intensive A", CourseKind.INTENSIVE, ct);
        await CreatePublishedCourseInDb("Intensive B", CourseKind.INTENSIVE, ct);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/courses/catalog?limit=10&kind=INTENSIVE", ct);

        response.EnsureSuccessStatusCode();
        CursorResponse<CourseCatalogDto> result =
            await ReadResultAsync<CursorResponse<CourseCatalogDto>>(response);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal("INTENSIVE", item.Kind));
    }

    [Fact]
    public async Task GetCatalog_WithoutKindFilter_ReturnsBothKinds()
    {
        CancellationToken ct = CancellationToken.None;
        await CreatePublishedCourseInDb("Course A", CourseKind.COURSE, ct);
        await CreatePublishedCourseInDb("Intensive A", CourseKind.INTENSIVE, ct);

        HttpResponseMessage response = await AppHttpClient.GetAsync("/courses/catalog?limit=10", ct);

        response.EnsureSuccessStatusCode();
        CursorResponse<CourseCatalogDto> result =
            await ReadResultAsync<CursorResponse<CourseCatalogDto>>(response);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, x => x.Kind == "COURSE");
        Assert.Contains(result.Items, x => x.Kind == "INTENSIVE");
    }

    [Fact]
    public async Task AttachIssueToModule_OnIntensive_Returns400()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAsAdmin(authorId);

        Guid courseId = await CreateCourseInDb(authorId, CourseKind.INTENSIVE, ct);
        Guid moduleId = await CreateModuleAttachedToCourseInDb(authorId, courseId, ct);
        Guid issueId = await CreateIssueInDb(authorId, ct);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/modules/{moduleId}/issues/{issueId}", content: null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("course.intensive.no.issues", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AttachIssueToModule_OnRegularCourse_Succeeds()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAsAdmin(authorId);

        Guid courseId = await CreateCourseInDb(authorId, CourseKind.COURSE, ct);
        Guid moduleId = await CreateModuleAttachedToCourseInDb(authorId, courseId, ct);
        Guid issueId = await CreateIssueInDb(authorId, ct);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/modules/{moduleId}/issues/{issueId}", content: null, ct);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CreateCourse_WithKindMarathon_StoresMarathon()
    {
        CancellationToken ct = CancellationToken.None;
        var request = new CreateCourseRequest(
            "Marathon Course", "Cohort recordings", "marathon-1", Kind: "MARATHON");

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync("/courses", request, ct);

        response.EnsureSuccessStatusCode();

        await ExecuteInDb(async db =>
        {
            Course course = await db.Courses.SingleAsync(ct);
            Assert.Equal(CourseKind.MARATHON, course.Kind);
        });
    }

    [Fact]
    public async Task GetCatalog_WithMarathonKindFilter_ReturnsOnlyMarathons()
    {
        CancellationToken ct = CancellationToken.None;
        await CreatePublishedCourseInDb("Course A", CourseKind.COURSE, ct);
        await CreatePublishedCourseInDb("Marathon A", CourseKind.MARATHON, ct);
        await CreatePublishedCourseInDb("Marathon B", CourseKind.MARATHON, ct);

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            "/courses/catalog?limit=10&kind=MARATHON", ct);

        response.EnsureSuccessStatusCode();
        CursorResponse<CourseCatalogDto> result =
            await ReadResultAsync<CursorResponse<CourseCatalogDto>>(response);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, item => Assert.Equal("MARATHON", item.Kind));
    }

    [Fact]
    public async Task AttachIssueToModule_OnMarathon_Returns400()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.NewGuid();
        AuthenticateAsAdmin(authorId);

        Guid courseId = await CreateCourseInDb(authorId, CourseKind.MARATHON, ct);
        Guid moduleId = await CreateModuleAttachedToCourseInDb(authorId, courseId, ct);
        Guid issueId = await CreateIssueInDb(authorId, ct);

        HttpResponseMessage response = await AppHttpClient.PostAsync(
            $"/modules/{moduleId}/issues/{issueId}", content: null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains("course.marathon.no.issues", body, StringComparison.Ordinal);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task<Guid> CreatePublishedCourseInDb(string title, CourseKind kind, CancellationToken ct)
    {
        Guid courseId = Guid.Empty;
        string uniquePrefix = Guid.NewGuid().ToString("N")[..8];

        await ExecuteInDb(async db =>
        {
            var course = new Course(
                Guid.CreateVersion7(),
                Title.Create(title).Value,
                Description.Create($"{title} description").Value,
                CourseSlug.Create($"slug-{uniquePrefix}").Value,
                SortKey.Initial(),
                kind);

            course.Publish();
            db.Courses.Add(course);
            courseId = course.Id;
            await db.SaveChangesAsync(ct);
        });

        return courseId;
    }

    private async Task<Guid> CreateCourseInDb(Guid authorId, CourseKind kind, CancellationToken ct)
    {
        Guid courseId = Guid.Empty;
        string uniquePrefix = Guid.NewGuid().ToString("N")[..8];

        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Course {uniquePrefix}").Value,
                Description.Create("Test course").Value,
                CourseSlug.Create($"course-{uniquePrefix}").Value,
                SortKey.Initial(),
                kind);
            db.Courses.Add(course);
            courseId = course.Id;
            await db.SaveChangesAsync(ct);
        });

        return courseId;
    }

    private async Task<Guid> CreateModuleAttachedToCourseInDb(
        Guid authorId, Guid courseId, CancellationToken ct)
    {
        Guid moduleId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var module = new Module(
                authorId,
                Title.Create($"Module {Guid.NewGuid().ToString("N")[..8]}").Value);
            db.Modules.Add(module);
            moduleId = module.Id;

            var courseItem = new CourseItem(
                courseId, CourseItemType.Module, moduleId, SortKey.Initial(), isOptional: false);
            db.CourseItems.Add(courseItem);

            await db.SaveChangesAsync(ct);
        });

        return moduleId;
    }

    private async Task<Guid> CreateIssueInDb(Guid authorId, CancellationToken ct)
    {
        Guid issueId = Guid.Empty;

        await ExecuteInDb(async db =>
        {
            var project = new Project(
                Guid.CreateVersion7(),
                Title.Create($"Project {Guid.NewGuid().ToString("N")[..8]}").Value,
                Description.Create("Test project").Value);
            db.Projects.Add(project);

            var issue = new Issue(
                authorId,
                project.Id,
                Title.Create($"Issue {Guid.NewGuid().ToString("N")[..8]}").Value,
                MarkdownContent.Create("Issue content").Value);
            db.Issues.Add(issue);
            issueId = issue.Id;

            await db.SaveChangesAsync(ct);
        });

        return issueId;
    }
}
