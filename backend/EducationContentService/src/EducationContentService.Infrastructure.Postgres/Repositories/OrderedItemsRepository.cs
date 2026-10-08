using System.Linq.Expressions;
using EducationContentService.Domain;
using Ordering;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public abstract class OrderedItemsRepository<T> : IOrderedItemsRepository<T>
    where T : class, IOrderedItem
{
    private readonly EducationDbContext _dbContext;
    private readonly string _containerName;

    protected OrderedItemsRepository(EducationDbContext dbContext, string containerName)
    {
        _dbContext = dbContext;
        _containerName = containerName;
    }

    public async Task AddAsync(T item, CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<T>().AddAsync(item, cancellationToken);
    }

    public async Task<Result<T, Error>> GetByAsync(
        Expression<Func<T, bool>> predicate,
        Expression<Func<T, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<T> query = _dbContext.Set<T>().Where(predicate);

        if (orderBy != null)
        {
            query = descending
                ? query.OrderByDescending(orderBy)
                : query.OrderBy(orderBy);
        }

        T? item = await query.FirstOrDefaultAsync(cancellationToken);

        return item is null
            ? EducationErrors.ItemNotFound(_containerName, Guid.Empty)
            : item;
    }

    public async Task<List<T>> GetManyByAsync(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        await _dbContext.Set<T>()
            .Where(predicate)
            .ToListAsync(cancellationToken);

    public void Delete(T item) => _dbContext.Set<T>().Remove(item);
}
