using EducationContentService.Core.Features.Collections;
using EducationContentService.Domain.Collections;

namespace EducationContentService.Infrastructure.Postgres.Repositories;

public sealed class CollectionSectionsRepository
    : OrderedItemsRepository<CollectionSection>, ICollectionSectionsRepository
{
    public CollectionSectionsRepository(EducationDbContext dbContext)
        : base(dbContext, "CollectionSection")
    {
    }
}
