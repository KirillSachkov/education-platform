using System.Linq.Expressions;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using EducationContentService.Domain.Projects;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class IssuesRepository : IIssuesRepository
{
    private readonly EducationDbContext _dbContext;

    public IssuesRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Issue issue, CancellationToken cancellationToken = default)
    {
        await _dbContext.Issues.AddAsync(issue, cancellationToken);
    }

    public async Task<Result<Issue, Error>> GetByAsync(
        Expression<Func<Issue, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Issue? issue = await _dbContext.Issues.FirstOrDefaultAsync(predicate, cancellationToken);

        return issue is null
            ? GeneralErrors.NotFound()
            : issue;
    }

    public async Task<List<Guid>> GetCourseIdsAsync(Guid issueId, CancellationToken ct = default)
    {
        return await _dbContext.Database.SqlQuery<Guid>(
                $"""
                 SELECT DISTINCT course_id
                 FROM (
                     SELECT ci.course_id
                     FROM issues i
                     JOIN course_items ci ON ci.reference_id = i.project_id AND ci.item_type = 'Project'
                     WHERE i.id = {issueId}

                     UNION

                     SELECT ci.course_id
                     FROM module_items mi
                     JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                     WHERE mi.item_type = 'Issue' AND mi.reference_id = {issueId}
                 ) issue_courses
                 """)
            .ToListAsync(ct);
    }

    public void Delete(Issue issue) => _dbContext.Issues.Remove(issue);

    public async Task<IReadOnlyList<Guid>> GetIdsByCourseItemAsync(
        CourseItemType itemType,
        Guid referenceId,
        CancellationToken ct = default)
    {
        return itemType switch
        {
            CourseItemType.Project => await _dbContext.Issues
                .Where(issue => issue.ProjectId == referenceId)
                .Select(issue => issue.Id)
                .ToListAsync(ct),
            CourseItemType.Module => await _dbContext.ModuleItems
                .Where(item => item.ModuleId == referenceId && item.ItemType == ModuleItemType.Issue)
                .Select(item => item.ReferenceId)
                .Distinct()
                .ToListAsync(ct),
            _ => [],
        };
    }

    public async Task<IReadOnlyList<Guid>> GetIdsByCourseIdAsync(
        Guid courseId,
        CancellationToken ct = default)
    {
        return await _dbContext.Database.SqlQuery<Guid>(
                $"""
                 SELECT DISTINCT issue_id AS "Value"
                 FROM (
                     SELECT i.id AS issue_id
                     FROM issues i
                     JOIN course_items ci ON ci.reference_id = i.project_id AND ci.item_type = 'Project'
                     WHERE ci.course_id = {courseId}

                     UNION

                     SELECT mi.reference_id AS issue_id
                     FROM module_items mi
                     JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                     WHERE ci.course_id = {courseId} AND mi.item_type = 'Issue'
                 ) affected_issues
                 """)
            .ToListAsync(ct);
    }

    public async Task<Guid?> GetCourseAuthorIdAsync(Guid issueId, CancellationToken ct = default)
    {
        // EF Core SqlQuery<Guid> ожидает колонку с именем "Value" (имя свойства
        // в скалярном wrapper-типе). Алиас обязателен — без него получаем
        // 42703: column s.Value does not exist.
        return await _dbContext.Database.SqlQuery<Guid>(
                $"""
                 SELECT DISTINCT c.author_id AS "Value"
                 FROM (
                     SELECT ci.course_id
                     FROM issues i
                     JOIN course_items ci ON ci.reference_id = i.project_id AND ci.item_type = 'Project'
                     WHERE i.id = {issueId}

                     UNION

                     SELECT ci.course_id
                     FROM module_items mi
                     JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                     WHERE mi.item_type = 'Issue' AND mi.reference_id = {issueId}
                 ) issue_courses
                 JOIN courses c ON c.id = issue_courses.course_id
                 """)
            .FirstOrDefaultAsync(ct);
    }
}
