using System.Linq.Expressions;
using EducationContentService.Core.Features.Roadmaps;
using EducationContentService.Domain.Roadmaps;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class RoadmapsRepository : IRoadmapsRepository
{
    private readonly EducationDbContext _dbContext;

    public RoadmapsRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Roadmap roadmap, CancellationToken cancellationToken = default)
    {
        await _dbContext.Roadmaps.AddAsync(roadmap, cancellationToken);
    }

    public async Task<Result<Roadmap, Error>> GetByAsync(
        Expression<Func<Roadmap, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Roadmap? roadmap = await _dbContext.Roadmaps
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return roadmap is null
            ? GeneralErrors.NotFound()
            : roadmap;
    }

    public async Task<Result<Roadmap, Error>> GetWithChildrenAsync(
        Expression<Func<Roadmap, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Roadmap? roadmap = await _dbContext.Roadmaps
            .Include(r => r.Nodes)
            .Include(r => r.Edges)
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return roadmap is null
            ? GeneralErrors.NotFound()
            : roadmap;
    }

    public Task<bool> ExistsAsync(
        Expression<Func<Roadmap, bool>> predicate,
        CancellationToken ct = default)
        => _dbContext.Roadmaps.AnyAsync(predicate, ct);

    public Task DeleteAsync(Roadmap roadmap, CancellationToken ct = default)
    {
        _dbContext.Roadmaps.Remove(roadmap);
        return Task.CompletedTask;
    }
}
