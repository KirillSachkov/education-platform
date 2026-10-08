using System.Linq.Expressions;
using EducationContentService.Domain.Collections;

namespace EducationContentService.Core.Features.Collections;

public interface ICollectionsRepository
{
    Task AddAsync(Collection collection, CancellationToken cancellationToken = default);

    void Delete(Collection collection);

    Task<Result<Collection, Error>> GetByAsync(
        Expression<Func<Collection, bool>> predicate,
        CancellationToken cancellationToken = default);

    Task<List<Collection>> GetManyByAsync(
        Expression<Func<Collection, bool>> predicate,
        CancellationToken cancellationToken = default);
}
