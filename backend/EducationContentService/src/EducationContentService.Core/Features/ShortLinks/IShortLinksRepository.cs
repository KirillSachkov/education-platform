using System.Linq.Expressions;
using EducationContentService.Domain.ShortLinks;

namespace EducationContentService.Core.Features.ShortLinks;

public interface IShortLinksRepository
{
    Task AddAsync(ShortLink shortLink, CancellationToken cancellationToken = default);

    Task<Result<ShortLink, Error>> GetByAsync(
        Expression<Func<ShortLink, bool>> predicate,
        CancellationToken cancellationToken = default);
}
