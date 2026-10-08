using System.Linq.Expressions;
using EducationContentService.Core.Features.Collections;
using EducationContentService.Domain.Collections;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class CollectionsRepository : ICollectionsRepository
{
    private readonly EducationDbContext _dbContext;

    public CollectionsRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(Collection collection, CancellationToken cancellationToken = default)
    {
        await _dbContext.Collections.AddAsync(collection, cancellationToken);
    }

    public void Delete(Collection collection)
    {
        _dbContext.Collections.Remove(collection);
    }

    public async Task<Result<Collection, Error>> GetByAsync(
        Expression<Func<Collection, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        Collection? collection = await _dbContext.Collections
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return collection is null
            ? GeneralErrors.NotFound()
            : collection;
    }

    public Task<List<Collection>> GetManyByAsync(
        Expression<Func<Collection, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Collections
            .Where(predicate)
            .ToListAsync(cancellationToken);
    }
}
