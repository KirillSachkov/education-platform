using EducationContentService.Core.Features.Collections;
using EducationContentService.Domain.Collections;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public sealed class CollectionItemsRepository
    : OrderedItemsRepository<CollectionItem>, ICollectionItemsRepository
{
    private readonly EducationDbContext _dbContext;

    public CollectionItemsRepository(EducationDbContext dbContext)
        : base(dbContext, "CollectionItem") => _dbContext = dbContext;

    public Task<bool> HasAnyForCollectionAsync(
        Guid collectionId, CancellationToken cancellationToken = default) =>
        _dbContext.CollectionItems
            .AnyAsync(
                i => _dbContext.CollectionSections
                    .Any(s => s.Id == i.SectionId && s.CollectionId == collectionId),
                cancellationToken);

    public Task<bool> ExistsInSectionAsync(
        Guid sectionId,
        CollectionItemType itemType,
        Guid referenceId,
        CancellationToken cancellationToken = default) =>
        _dbContext.CollectionItems
            .AnyAsync(
                i => i.SectionId == sectionId && i.ItemType == itemType && i.ReferenceId == referenceId,
                cancellationToken);
}
