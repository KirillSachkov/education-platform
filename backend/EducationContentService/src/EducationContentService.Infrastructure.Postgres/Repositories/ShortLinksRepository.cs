using System.Linq.Expressions;
using EducationContentService.Core.Features.ShortLinks;
using EducationContentService.Domain.ShortLinks;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public class ShortLinksRepository : IShortLinksRepository
{
    private readonly EducationDbContext _dbContext;

    public ShortLinksRepository(EducationDbContext dbContext) => _dbContext = dbContext;

    public async Task AddAsync(ShortLink shortLink, CancellationToken cancellationToken = default)
    {
        await _dbContext.ShortLinks.AddAsync(shortLink, cancellationToken);
    }

    public async Task<Result<ShortLink, Error>> GetByAsync(
        Expression<Func<ShortLink, bool>> predicate,
        CancellationToken cancellationToken = default)
    {
        ShortLink? shortLink = await _dbContext.ShortLinks
            .FirstOrDefaultAsync(predicate, cancellationToken);

        return shortLink is null
            ? GeneralErrors.NotFound()
            : shortLink;
    }
}
