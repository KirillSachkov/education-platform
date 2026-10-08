using System.Linq.Expressions;
using EducationContentService.Core.Features.ProjectItems;
using EducationContentService.Domain;
using EducationContentService.Domain.Projects;
using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class ProjectsRepository : IProjectsRepository
{
    private readonly EducationDbContext _dbContext;

    public ProjectsRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default)
    {
        await _dbContext.Projects.AddAsync(project, cancellationToken);
    }

    public async Task<Result<Project, Error>> GetByAsync(
        Expression<Func<Project, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Project? project = await _dbContext.Projects.FirstOrDefaultAsync(predicate, cancellationToken);

        return project is null
            ? GeneralErrors.NotFound()
            : project;
    }

    public Task<bool> ExistsAsync(
        Expression<Func<Project, bool>> predicate,
        CancellationToken cancellationToken = default)
        => _dbContext.Projects.AnyAsync(predicate, cancellationToken);

    public Task<bool> ExistsByTitleAsync(Title title, Guid excludeId, CancellationToken ct = default)
        => _dbContext.Projects
            .AnyAsync(p => p.Title == title && p.Status != PublicationStatus.DRAFT && p.Id != excludeId, ct);
}
