using System.Linq.Expressions;
using ProgressService.Core.Abstractions;
using ProgressService.Domain.ContentAccess;

namespace ProgressService.Infrastructure.Postgres.Repositories;

public sealed class ContentGrantRepository : IContentGrantRepository
{
    private readonly ProgressDbContext _dbContext;

    public ContentGrantRepository(ProgressDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ContentGrant?> GetByAsync(
        Expression<Func<ContentGrant, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbContext.ContentGrants.FirstOrDefaultAsync(predicate, ct);
    }

    public async Task<IReadOnlyList<ContentGrant>> GetManyByAsync(
        Expression<Func<ContentGrant, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbContext.ContentGrants.Where(predicate).ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(
        Expression<Func<ContentGrant, bool>> predicate,
        CancellationToken ct = default)
    {
        return await _dbContext.ContentGrants.AnyAsync(predicate, ct);
    }

    public async Task AddAsync(ContentGrant grant, CancellationToken ct = default)
    {
        await _dbContext.ContentGrants.AddAsync(grant, ct);
    }

    public async Task AddRangeAsync(IReadOnlyCollection<ContentGrant> grants, CancellationToken ct = default)
    {
        await _dbContext.ContentGrants.AddRangeAsync(grants, ct);
    }
}
