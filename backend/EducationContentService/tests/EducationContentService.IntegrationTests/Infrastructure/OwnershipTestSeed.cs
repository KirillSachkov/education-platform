using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;
using EducationContentService.Infrastructure.Postgres;
using Microsoft.EntityFrameworkCore;
using Ordering;

namespace EducationContentService.IntegrationTests.Infrastructure;

/// <summary>
///     Helpers for the cross-author ownership-rejection test suite (issue #262).
///     Each helper writes one aggregate owned by the given <c>authorId</c> directly via
///     <see cref="EducationDbContext" /> so tests can wire two-author scenarios without
///     going through HTTP create endpoints (which auto-set author = caller).
/// </summary>
internal static class OwnershipTestSeed
{
    public static async Task<Guid> CreateCourseAsync(
        EducationDbContext db, Guid authorId, string suffix, CancellationToken ct = default)
    {
        string slug = $"ownership-course-{suffix}-{Guid.NewGuid().ToString("N")[..6]}";
        var course = new Course(
            authorId,
            Title.Create($"Course {suffix}").Value,
            Description.Create($"Course {suffix} description").Value,
            CourseSlug.Create(slug).Value,
            SortKey.Initial());
        db.Courses.Add(course);
        await db.SaveChangesAsync(ct);
        return course.Id;
    }

    public static async Task<Guid> CreateModuleAsync(
        EducationDbContext db,
        Guid authorId,
        string suffix,
        bool published = false,
        CancellationToken ct = default)
    {
        var module = new Module(
            authorId,
            Title.Create($"Module {suffix}").Value,
            Description.Create($"Module {suffix} description").Value);
        if (published)
        {
            module.Publish();
        }
        db.Modules.Add(module);
        await db.SaveChangesAsync(ct);
        return module.Id;
    }

    public static async Task<Guid> CreateProjectAsync(
        EducationDbContext db,
        Guid authorId,
        string suffix,
        CancellationToken ct = default)
    {
        var project = new Project(
            authorId,
            Title.Create($"Project {suffix}").Value,
            Description.Create($"Project {suffix} description").Value);
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project.Id;
    }

    /// <summary>
    ///     Creates a draft issue under the given project. Issue author = parent project author.
    /// </summary>
    public static async Task<Guid> CreateIssueAsync(
        EducationDbContext db,
        Guid authorId,
        Guid projectId,
        string suffix,
        CancellationToken ct = default)
    {
        var issue = new Issue(
            authorId,
            projectId,
            Title.Create($"Issue {suffix}").Value,
            MarkdownContent.Create($"Content of issue {suffix}").Value);
        db.Issues.Add(issue);
        await db.SaveChangesAsync(ct);
        return issue.Id;
    }

    /// <summary>
    ///     Inserts a <see cref="CourseItem" /> linking a module/project to a course at the next sort key.
    ///     Mirrors what the API would do via <c>CourseItemService.CreateAsync</c>.
    /// </summary>
    public static async Task<Guid> AttachItemToCourseAsync(
        EducationDbContext db,
        Guid courseId,
        CourseItemType itemType,
        Guid referenceId,
        CancellationToken ct = default)
    {
        string? lastSortKey = await db.CourseItems
            .Where(ci => ci.CourseId == courseId)
            .OrderByDescending(ci => ci.SortKey)
            .Select(ci => ci.SortKey.Value)
            .FirstOrDefaultAsync(ct);

        SortKey nextKey = lastSortKey is null
            ? SortKey.Initial()
            : SortKey.After(SortKey.Create(lastSortKey).Value);

        var item = new CourseItem(courseId, itemType, referenceId, nextKey, isOptional: false);
        db.CourseItems.Add(item);
        await db.SaveChangesAsync(ct);
        return item.Id;
    }
}
