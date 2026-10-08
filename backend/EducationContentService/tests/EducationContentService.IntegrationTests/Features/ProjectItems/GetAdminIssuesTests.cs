using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Issues;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.ProjectItems;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetAdminIssuesTests : EducationContentServiceTestsBase
{
    public GetAdminIssuesTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task AdminList_Anonymous_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.GetAsync("/issues/admin-list");

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, got {response.StatusCode}");
    }

    [Fact]
    public async Task AdminList_NonAdminAuthor_Returns403()
    {
        // Admin-list returns issues across ALL authors → admin/service-only. A platform-author
        // (who still holds Issues.MANAGE for their own content) must NOT reach it — was a
        // cross-author IDOR before the endpoint moved from RequirePermissions to RequireAnyRole.
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync("/issues/admin-list");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminList_FilterByCourse_ReturnsIssueWithCoursePlacement()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid projectId = await CreateProjectAsync(ct);
        Guid issueId = await CreateIssueAsync(authorId, projectId, title: "Уникальное задание", publish: false, ct);
        Guid courseId = await CreatePublishedCourseAsync(authorId, slug: "course-x", ct);
        await AttachProjectToCourseAsync(courseId, projectId, ct);

        AuthenticateAsAdmin();

        IReadOnlyList<AdminIssueListItemDto> rows =
            await GetRows($"/issues/admin-list?courseId={courseId}");

        AdminIssueListItemDto row = Assert.Single(rows);
        Assert.Equal(issueId, row.IssueId);
        Assert.Equal(projectId, row.ProjectId);
        Assert.Equal(courseId, row.CourseId);
        Assert.Equal("Проект", row.ProjectTitle);
        Assert.Null(row.Content); // includeContent defaults false
    }

    [Fact]
    public async Task AdminList_FilterByStatus_OnlyMatchingStatus()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid projectId = await CreateProjectAsync(ct);
        Guid draftId = await CreateIssueAsync(authorId, projectId, title: "Черновик", publish: false, ct);
        Guid publishedId = await CreateIssueAsync(authorId, projectId, title: "Опубликовано", publish: true, ct);

        AuthenticateAsAdmin();

        IReadOnlyList<AdminIssueListItemDto> published =
            await GetRows($"/issues/admin-list?projectId={projectId}&status=PUBLISHED");

        Assert.Single(published);
        Assert.Equal(publishedId, published[0].IssueId);
        Assert.DoesNotContain(published, r => r.IssueId == draftId);
    }

    [Fact]
    public async Task Search_MatchesContent_AndIncludeContentReturnsBody()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid projectId = await CreateProjectAsync(ct);
        const string needle = "корреляционныйидентификатор";
        Guid issueId = await CreateIssueAsync(
            authorId, projectId, title: "Задача", content: $"# Тело\n\n{needle} внутри", publish: false, ct);

        AuthenticateAsAdmin();

        IReadOnlyList<AdminIssueListItemDto> hits =
            await GetRows($"/issues/admin-list?search={needle}&includeContent=true");

        AdminIssueListItemDto hit = Assert.Single(hits);
        Assert.Equal(issueId, hit.IssueId);
        Assert.NotNull(hit.Content);
        Assert.Contains(needle, hit.Content!, StringComparison.Ordinal);

        IReadOnlyList<AdminIssueListItemDto> miss =
            await GetRows("/issues/admin-list?search=несуществующаяфраза");
        Assert.DoesNotContain(miss, r => r.IssueId == issueId);
    }

    [Fact]
    public async Task Export_CourseFilter_IncludeContent_ReturnsBody()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid projectId = await CreateProjectAsync(ct);
        const string marker = "телотелатело";
        Guid issueId = await CreateIssueAsync(
            authorId, projectId, title: "С контентом", content: $"# Тело\n\n{marker}", publish: true, ct);
        Guid courseId = await CreatePublishedCourseAsync(authorId, slug: "export-course", ct);
        await AttachProjectToCourseAsync(courseId, projectId, ct);

        AuthenticateAsAdmin();

        IReadOnlyList<AdminIssueListItemDto> rows =
            await GetRows($"/issues/admin-list?courseId={courseId}&includeContent=true");

        AdminIssueListItemDto row = Assert.Single(rows);
        Assert.Equal(issueId, row.IssueId);
        Assert.Equal(courseId, row.CourseId);
        Assert.NotNull(row.Content);
        Assert.Contains(marker, row.Content!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminList_ProjectInTwoCourses_IssueAppearsOnce_WithPrimaryCourse()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid projectId = await CreateProjectAsync(ct);
        Guid issueId = await CreateIssueAsync(authorId, projectId, title: "В двух курсах", publish: false, ct);

        // Project attached to two courses; alpha first → primary (earliest course_items.id, Guid v7).
        Guid alphaId = await CreatePublishedCourseAsync(authorId, slug: "alpha-2c", ct);
        Guid betaId = await CreatePublishedCourseAsync(authorId, slug: "beta-2c", ct);
        await AttachProjectToCourseAsync(alphaId, projectId, ct);
        await Task.Delay(5, ct);
        await AttachProjectToCourseAsync(betaId, projectId, ct);

        AuthenticateAsAdmin();

        IReadOnlyList<AdminIssueListItemDto> rows =
            await GetRows($"/issues/admin-list?projectId={projectId}");

        // No fan-out: exactly one row despite two course attachments, anchored to the primary course.
        AdminIssueListItemDto row = Assert.Single(rows.Where(r => r.IssueId == issueId).ToList());
        Assert.Equal(alphaId, row.CourseId);
    }

    private async Task<IReadOnlyList<AdminIssueListItemDto>> GetRows(string url)
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<AdminIssueListItemDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<AdminIssueListItemDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        return envelope.Result;
    }

    private async Task<Guid> CreateProjectAsync(CancellationToken ct)
    {
        Guid projectId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var project = new Project(
                Guid.CreateVersion7(),
                Title.Create("Проект").Value,
                Description.Create("Описание").Value);
            db.Projects.Add(project);
            projectId = project.Id;
            await db.SaveChangesAsync(ct);
        });
        return projectId;
    }

    private async Task<Guid> CreateIssueAsync(
        Guid authorId, Guid projectId, string title, bool publish, CancellationToken ct) =>
        await CreateIssueAsync(authorId, projectId, title, content: "# Задача\n\nТело", publish, ct);

    private async Task<Guid> CreateIssueAsync(
        Guid authorId, Guid projectId, string title, string content, bool publish, CancellationToken ct)
    {
        Guid issueId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var issue = new Issue(
                authorId,
                projectId,
                Title.Create(title).Value,
                MarkdownContent.Create(content).Value);
            if (publish)
                Assert.True(issue.Publish().IsSuccess);
            db.Issues.Add(issue);
            issueId = issue.Id;
            await db.SaveChangesAsync(ct);
        });
        return issueId;
    }

    private async Task<Guid> CreatePublishedCourseAsync(Guid authorId, string slug, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {slug}").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create(slug).Value, SortKey.Initial());
            Assert.True(course.Publish().IsSuccess);
            db.Courses.Add(course);
            await db.SaveChangesAsync(ct);
            id = course.Id;
        });
        return id;
    }

    private async Task AttachProjectToCourseAsync(Guid courseId, Guid projectId, CancellationToken ct)
    {
        await ExecuteInDb(async db =>
        {
            db.CourseItems.Add(new CourseItem(
                courseId,
                CourseItemType.Project,
                projectId,
                SortKey.Initial(),
                isOptional: false));
            await db.SaveChangesAsync(ct);
        });
    }
}
