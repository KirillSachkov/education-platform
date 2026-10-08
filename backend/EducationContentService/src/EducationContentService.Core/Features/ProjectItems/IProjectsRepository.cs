using System.Linq.Expressions;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Core.Features.ProjectItems;

public interface IProjectsRepository
{
    Task AddAsync(Project project, CancellationToken cancellationToken = default);

    Task<Result<Project, Error>> GetByAsync(
        Expression<Func<Project, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        Expression<Func<Project, bool>> predicate,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Уникальность заголовка среди non-DRAFT проектов (PUBLISHED + ARCHIVED).
    /// </summary>
    Task<bool> ExistsByTitleAsync(Title title, Guid excludeId, CancellationToken ct = default);
}
