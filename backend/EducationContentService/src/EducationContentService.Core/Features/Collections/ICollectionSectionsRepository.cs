using EducationContentService.Domain.Collections;
using Ordering;

namespace EducationContentService.Core.Features.Collections;

public interface ICollectionSectionsRepository : IOrderedItemsRepository<CollectionSection>;
