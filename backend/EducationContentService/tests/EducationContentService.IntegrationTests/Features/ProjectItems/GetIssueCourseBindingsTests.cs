using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Issues;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features.ProjectItems;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class GetIssueCourseBindingsTests : EducationContentServiceTestsBase
{
    public GetIssueCourseBindingsTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Endpoint_Anonymous_Returns401()
    {
        RemoveAuthentication();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/issues/course-bindings",
            new GetIssueCourseBindingsRequest(new[] { Guid.NewGuid() }));

        Assert.True(
            response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
            $"expected 401 or 403, got {response.StatusCode}");
    }

    [Fact]
    public async Task Endpoint_ReturnsPrimaryCourseBindingPerIssue()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid projectId = await CreateProjectAsync(ct);
        Guid issueId = await CreateIssueAsync(authorId, projectId, ct);

        // Привязываем проект к двум опубликованным курсам — alpha добавлен раньше,
        // должен стать primary (ORDER BY ci.id, Guid v7 time-ordered).
        Guid course1Id = await CreatePublishedCourseAsync(authorId, slug: "alpha", ct);
        Guid course2Id = await CreatePublishedCourseAsync(authorId, slug: "beta", ct);

        await AttachProjectToCourseAsync(course1Id, projectId, ct);
        await Task.Delay(5, ct);
        await AttachProjectToCourseAsync(course2Id, projectId, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/issues/course-bindings",
            new GetIssueCourseBindingsRequest(new[] { issueId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<IssueCourseBindingLookupDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<IssueCourseBindingLookupDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);

        Assert.Single(envelope.Result);
        Assert.Equal(issueId, envelope.Result[0].IssueId);
        Assert.Equal(course1Id, envelope.Result[0].CourseId);
        Assert.Equal("alpha", envelope.Result[0].CourseSlug);
    }

    [Fact]
    public async Task Endpoint_DraftCourse_NotReturned()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid projectId = await CreateProjectAsync(ct);
        Guid issueId = await CreateIssueAsync(authorId, projectId, ct);
        Guid draftCourseId = await CreateDraftCourseAsync(authorId, slug: "draft", ct);

        await AttachProjectToCourseAsync(draftCourseId, projectId, ct);

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/issues/course-bindings",
            new GetIssueCourseBindingsRequest(new[] { issueId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<IssueCourseBindingLookupDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<IssueCourseBindingLookupDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result);
    }

    [Fact]
    public async Task Endpoint_OrphanIssue_NotInResponse()
    {
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();

        Guid projectId = await CreateProjectAsync(ct);
        Guid issueId = await CreateIssueAsync(authorId, projectId, ct);
        // Проект не приклеен ни к одному курсу — issue остаётся orphan.

        AuthenticateAsAdmin();

        HttpResponseMessage response = await AppHttpClient.PostAsJsonAsync(
            "/internal/issues/course-bindings",
            new GetIssueCourseBindingsRequest(new[] { issueId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Envelope<IReadOnlyList<IssueCourseBindingLookupDto>>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<IReadOnlyList<IssueCourseBindingLookupDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Result);
        Assert.Empty(envelope.Result);
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

    private async Task<Guid> CreateIssueAsync(Guid authorId, Guid projectId, CancellationToken ct)
    {
        Guid issueId = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var issue = new Issue(
                authorId,
                projectId,
                Title.Create("Задача").Value,
                MarkdownContent.Create("# Задача\n\nТело").Value);
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

    private async Task<Guid> CreateDraftCourseAsync(Guid authorId, string slug, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Курс {slug}").Value,
                Description.Create("Описание").Value,
                slug: CourseSlug.Create(slug).Value, SortKey.Initial());
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
