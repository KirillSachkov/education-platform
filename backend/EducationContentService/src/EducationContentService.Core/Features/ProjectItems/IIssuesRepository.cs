using System.Linq.Expressions;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Projects;

namespace EducationContentService.Core.Features.ProjectItems;

public interface IIssuesRepository
{
    Task AddAsync(Issue issue, CancellationToken cancellationToken = default);

    Task<Result<Issue, Error>> GetByAsync(
        Expression<Func<Issue, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<List<Guid>> GetCourseIdsAsync(Guid issueId, CancellationToken ct = default);

    Task<Guid?> GetCourseAuthorIdAsync(Guid issueId, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetIdsByCourseItemAsync(
        CourseItemType itemType,
        Guid referenceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetIdsByCourseIdAsync(Guid courseId, CancellationToken ct = default);

    void Delete(Issue issue);
}
