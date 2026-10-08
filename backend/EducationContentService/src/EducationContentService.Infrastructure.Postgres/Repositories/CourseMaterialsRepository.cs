using System.Linq.Expressions;
using EducationContentService.Core.Features.CourseMaterials;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class CourseMaterialsRepository : ICourseMaterialsRepository
{
    private readonly EducationDbContext _dbContext;

    public CourseMaterialsRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(CourseMaterial item, CancellationToken cancellationToken = default)
    {
        await _dbContext.CourseMaterials.AddAsync(item, cancellationToken);
    }

    public async Task<Result<CourseMaterial, Error>> GetByAsync(
        Expression<Func<CourseMaterial, bool>> predicate,
        Expression<Func<CourseMaterial, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<CourseMaterial> query = _dbContext.CourseMaterials.Where(predicate);

        if (orderBy != null)
        {
            query = descending
                ? query.OrderByDescending(orderBy)
                : query.OrderBy(orderBy);
        }

        CourseMaterial? item = await query.FirstOrDefaultAsync(cancellationToken);

        return item is null
            ? EducationErrors.ItemNotFound("Course", Guid.Empty)
            : item;
    }

    public async Task<List<CourseMaterial>> GetManyByAsync(
        Expression<Func<CourseMaterial, bool>> predicate,
        CancellationToken cancellationToken = default) =>
        await _dbContext.CourseMaterials
            .Where(predicate)
            .ToListAsync(cancellationToken);

    public void Delete(CourseMaterial item) => _dbContext.CourseMaterials.Remove(item);
}
