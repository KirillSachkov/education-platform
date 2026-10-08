using System.Linq.Expressions;
using EducationContentService.Domain.Courses;

namespace EducationContentService.Core.Features.CourseMaterials;

public interface ICourseMaterialsRepository
{
    Task AddAsync(CourseMaterial item, CancellationToken cancellationToken = default);

    Task<Result<CourseMaterial, Error>> GetByAsync(
        Expression<Func<CourseMaterial, bool>> predicate,
        Expression<Func<CourseMaterial, object>>? orderBy = null,
        bool descending = false,
        CancellationToken cancellationToken = default);

    Task<List<CourseMaterial>> GetManyByAsync(
        Expression<Func<CourseMaterial, bool>> predicate,
        CancellationToken cancellationToken = default);

    void Delete(CourseMaterial item);
}
