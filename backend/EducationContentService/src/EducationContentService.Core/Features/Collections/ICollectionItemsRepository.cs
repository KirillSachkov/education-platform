using EducationContentService.Domain.Collections;
using Ordering;

namespace EducationContentService.Core.Features.Collections;

public interface ICollectionItemsRepository : IOrderedItemsRepository<CollectionItem>
{
    /// <summary>
    ///     Проверяет, есть ли в подборке хотя бы один элемент — нужно для инварианта
    ///     <see cref="Collection.Publish"/>, чтобы не тянуть секции и элементы в память.
    /// </summary>
    Task<bool> HasAnyForCollectionAsync(Guid collectionId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Существует ли уже элемент с данной generic-ссылкой
    ///     (<paramref name="itemType"/> + <paramref name="referenceId"/>) в секции
    ///     <paramref name="sectionId"/>. Дублирует БД-уникальный индекс
    ///     <c>ux_collection_items_section_reference</c>, но позволяет вернуть
    ///     понятную доменную ошибку до INSERT.
    /// </summary>
    Task<bool> ExistsInSectionAsync(
        Guid sectionId,
        CollectionItemType itemType,
        Guid referenceId,
        CancellationToken cancellationToken = default);
}
