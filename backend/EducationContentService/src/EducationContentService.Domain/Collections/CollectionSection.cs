using Ordering;

namespace EducationContentService.Domain.Collections;

/// <summary>
///     Секция подборки — отдельная сущность, ссылающаяся на <see cref="Collection"/>
///     по FK. Загружается и модифицируется через <c>ICollectionSectionsRepository</c>,
///     не через навигацию из aggregate root.
/// </summary>
public sealed class CollectionSection : IOrderedItem
{
    // EF Core constructor
    private CollectionSection() { }

    public CollectionSection(Guid collectionId, string? title, string? description, SortKey sortKey)
    {
        Id = Guid.CreateVersion7();
        CollectionId = collectionId;
        Title = title;
        Description = description;
        SortKey = sortKey;
    }

    public Guid Id { get; private set; }
    public Guid CollectionId { get; private set; }
    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public SortKey SortKey { get; private set; } = null!;

    public void Update(string? title, string? description)
    {
        Title = title;
        Description = description;
    }

    public void UpdateSortKey(SortKey sortKey) => SortKey = sortKey;
}
