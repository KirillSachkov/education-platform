using System.Net;
using System.Net.Http.Json;
using EducationContentService.Contracts.Projects;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.IntegrationTests.Infrastructure;
using Ordering;
using SharedKernel;

namespace EducationContentService.IntegrationTests.Features;

[Collection(nameof(IntegrationTestsFixture))]
public sealed class CourseReviewCoverageTests : EducationContentServiceTestsBase
{
    public CourseReviewCoverageTests(IntegrationTestsWebFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task GetCourseReviewCoverage_AggregatesProjectAndModulePlacements_ReturnsSummary()
    {
        // Arrange — a course with TWO sources of issues:
        //   • Project A attached as a course_item Project, 2 issues via project_items.
        //   • Project B reachable only because one of its issues is attached to a module
        //     of the course (module_items, item_type=Issue). Proves coverage covers
        //     module-placed tasks too, and surfaces the owning project even when the
        //     project itself is not a direct course_item.
        CancellationToken ct = CancellationToken.None;
        Guid authorId = Guid.CreateVersion7();
        Guid courseId = await CreateCourseAsync(authorId, "coverage-course", ct);

        Guid projectA = await CreateProjectAsync(authorId, "Project A", ct);
        await LinkProjectToCourseAsync(courseId, projectA, ct);
        Guid issueA1 = await CreateIssueAsync(authorId, projectA, "A1", ct);
        Guid issueA2 = await CreateIssueAsync(authorId, projectA, "A2", ct);
        await LinkIssueToProjectAsync(projectA, issueA1, ct);
        await LinkIssueToProjectAsync(projectA, issueA2, ct);

        Guid projectB = await CreateProjectAsync(authorId, "Project B", ct);
        Guid issueB1 = await CreateIssueAsync(authorId, projectB, "B1", ct);
        Guid moduleId = await CreateModuleAsync(authorId, "Module 1", ct);
        await LinkModuleToCourseAsync(courseId, moduleId, ct);
        await LinkIssueToModuleAsync(moduleId, issueB1, ct);

        const string guidelines = "Project A review guidelines markdown.";
        const string authorPrompt = "Check the repository pattern usage.";
        const string aspects = "Focus on error handling.";

        await ExecuteInDb(async db =>
        {
            db.ProjectReviewContexts.Add(
                ProjectReviewContext.Create(projectA, guidelines, isAutoReviewEnabled: true));
            // issueA1 gets a full spec with auto-review explicitly disabled.
            db.ReviewSpecs.Add(
                ReviewSpec.Create(issueA1, projectA, authorPrompt, aspects, isAutoReviewEnabled: false));
            await db.SaveChangesAsync(ct);
        });

        // Act
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/courses/{courseId}/review-coverage", ct);

        // Assert
        response.EnsureSuccessStatusCode();
        Envelope<CourseReviewCoverageDto>? envelope =
            await response.Content.ReadFromJsonAsync<Envelope<CourseReviewCoverageDto>>(ct);
        Assert.NotNull(envelope);
        CourseReviewCoverageDto dto = envelope!.Result!;

        Assert.Equal(courseId, dto.CourseId);
        Assert.Equal(2, dto.Projects.Count);

        // Rollup summary across the whole course.
        Assert.Equal(2, dto.Summary.ProjectCount);
        Assert.Equal(1, dto.Summary.ProjectsWithContext);
        Assert.Equal(0, dto.Summary.ProjectsAutoReviewDisabled);
        Assert.Equal(3, dto.Summary.IssueCount);
        Assert.Equal(1, dto.Summary.IssuesWithReviewSpec);
        Assert.Equal(1, dto.Summary.IssuesWithAuthorPrompt);
        Assert.Equal(1, dto.Summary.IssuesWithReviewAspects);
        Assert.Equal(1, dto.Summary.IssuesAutoReviewDisabled);

        // Per-project breakdown — project A has context + its two issues.
        ProjectReviewCoverageDto blockA = dto.Projects.Single(p => p.ProjectId == projectA);
        Assert.True(blockA.HasProjectContext);
        Assert.Equal(guidelines.Length, blockA.GuidelinesLength);
        Assert.Equal(2, blockA.Issues.Count);
        IssueReviewCoverageDto a1 = blockA.Issues.Single(i => i.IssueId == issueA1);
        Assert.True(a1.HasReviewSpec);
        Assert.Equal(authorPrompt.Length, a1.AuthorPromptLength);
        Assert.False(a1.IsAutoReviewEnabled);
        Assert.True(blockA.Issues.Single(i => i.IssueId == issueA2).IsAutoReviewEnabled); // default

        // Project B surfaced via its module-placed issue, no context yet.
        ProjectReviewCoverageDto blockB = dto.Projects.Single(p => p.ProjectId == projectB);
        Assert.False(blockB.HasProjectContext);
        Assert.True(blockB.ProjectIsAutoReviewEnabled); // effective default
        Assert.Single(blockB.Issues);
        Assert.Equal(issueB1, blockB.Issues[0].IssueId);
        Assert.False(blockB.Issues[0].HasReviewSpec);
    }

    [Fact]
    public async Task GetCourseReviewCoverage_NonExistentCourse_ReturnsNotFound()
    {
        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/courses/{Guid.NewGuid()}/review-coverage", CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCourseReviewCoverage_NonOwnerNonAdmin_ReturnsForbidden()
    {
        CancellationToken ct = CancellationToken.None;
        Guid courseId = await CreateCourseAsync(Guid.CreateVersion7(), "owned-course", ct);

        // An author who has Issues.MANAGE but does not own this course.
        AuthenticateAs(Guid.NewGuid(), "platform-author");

        HttpResponseMessage response = await AppHttpClient.GetAsync(
            $"/courses/{courseId}/review-coverage", ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreateCourseAsync(Guid authorId, string slug, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var course = new Course(
                authorId,
                Title.Create($"Course {slug}").Value,
                Description.Create("Course description").Value,
                slug: CourseSlug.Create(slug).Value,
                SortKey.Initial());
            db.Courses.Add(course);
            await db.SaveChangesAsync(ct);
            id = course.Id;
        });
        return id;
    }

    private async Task<Guid> CreateProjectAsync(Guid authorId, string title, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var project = new Project(
                authorId,
                Title.Create(title).Value,
                Description.Create($"{title} description").Value);
            db.Projects.Add(project);
            await db.SaveChangesAsync(ct);
            id = project.Id;
        });
        return id;
    }

    private async Task<Guid> CreateModuleAsync(Guid authorId, string title, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var module = new Module(
                authorId,
                Title.Create(title).Value,
                Description.Create($"{title} description").Value);
            db.Modules.Add(module);
            await db.SaveChangesAsync(ct);
            id = module.Id;
        });
        return id;
    }

    private async Task<Guid> CreateIssueAsync(Guid authorId, Guid projectId, string title, CancellationToken ct)
    {
        Guid id = Guid.Empty;
        await ExecuteInDb(async db =>
        {
            var issue = new Issue(
                authorId,
                projectId,
                Title.Create($"Issue {title}").Value,
                MarkdownContent.Create($"# {title}\n\nbody").Value);
            db.Issues.Add(issue);
            await db.SaveChangesAsync(ct);
            id = issue.Id;
        });
        return id;
    }

    private Task LinkProjectToCourseAsync(Guid courseId, Guid projectId, CancellationToken ct) =>
        ExecuteInDb(async db =>
        {
            db.CourseItems.Add(new CourseItem(
                courseId, CourseItemType.Project, projectId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
        });

    private Task LinkModuleToCourseAsync(Guid courseId, Guid moduleId, CancellationToken ct) =>
        ExecuteInDb(async db =>
        {
            db.CourseItems.Add(new CourseItem(
                courseId, CourseItemType.Module, moduleId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
        });

    private Task LinkIssueToProjectAsync(Guid projectId, Guid issueId, CancellationToken ct) =>
        ExecuteInDb(async db =>
        {
            db.ProjectItems.Add(new ProjectItem(
                projectId, issueId, SortKey.Initial(), isOptional: false, maxScore: null));
            await db.SaveChangesAsync(ct);
        });

    private Task LinkIssueToModuleAsync(Guid moduleId, Guid issueId, CancellationToken ct) =>
        ExecuteInDb(async db =>
        {
            db.ModuleItems.Add(new ModuleItem(
                moduleId, ModuleItemType.Issue, issueId, SortKey.Initial(), isOptional: false));
            await db.SaveChangesAsync(ct);
        });
}
