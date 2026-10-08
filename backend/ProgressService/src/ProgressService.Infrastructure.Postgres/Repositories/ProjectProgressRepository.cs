using System.Linq.Expressions;
using ProgressService.Core.Abstractions;
using ProgressService.Domain;
using ProgressService.Domain.Projects;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class ProjectProgressRepository : IProjectProgressRepository
{
    private readonly ProgressDbContext _dbContext;

    public ProjectProgressRepository(ProgressDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(
        ProjectProgress projectProgress,
        CancellationToken cancellationToken = default)
    {
        await _dbContext.ProjectProgresses.AddAsync(projectProgress, cancellationToken);
    }

    public async Task<Result<ProjectProgress, Error>> GetByAsync(
        Expression<Func<ProjectProgress, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ProjectProgress? projectProgress = await _dbContext.ProjectProgresses
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return projectProgress is null
            ? ProgressErrors.ProjectProgressNotFound()
            : projectProgress;
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<ProjectProgress, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProjectProgresses.AnyAsync(predicate, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectProgress>> GetManyByAsync(
        Expression<Func<ProjectProgress, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProjectProgresses
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }
}
