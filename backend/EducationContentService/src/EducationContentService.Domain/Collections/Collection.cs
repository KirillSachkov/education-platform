using EducationContentService.Domain.ValueObjects;
using Ordering;

namespace EducationContentService.Domain.Collections;

/// <summary>
///     Aggregate Root подборки. Держит только собственные поля: секции и элементы —
///     отдельные сущности, доступные через <see cref="CollectionSection"/> / <see cref="CollectionItem"/>
///     и свои репозитории. Aggregate сознательно не знает о детях: load'ить дерево одним запросом
///     запрещено, хендлеры достают только то, что им нужно (см. паттерн Module / ModuleItem).
/// </summary>
public sealed class Collection
{
    // EF Core constructor
    private Collection() { }

    public Collection(Guid authorId, Title title, Guid? courseId = null, AccessType? accessType = null)
    {
        Id = Guid.CreateVersion7();
        AuthorId = authorId;
        Title = title;
        CourseId = courseId;
        // Default: course-level → ENROLLED; space-level → PUBLIC. Keeps parity с Material.
        AccessType = accessType ?? (courseId is null ? AccessType.PUBLIC : AccessType.ENROLLED);
        Status = PublicationStatus.DRAFT;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid AuthorId { get; private set; }
    public Title Title { get; private set; } = null!;
    public Description? Description { get; private set; }
    public Guid? CoverImageId { get; private set; }
    public long CoverBindingRevision { get; private set; }
    public long MediaVersion { get; private set; }
    public Guid? CourseId { get; private set; }
    public AccessType AccessType { get; private set; }
    public PublicationStatus Status { get; private set; }
    public bool IsPinned { get; private set; }
    public SortKey? PinnedSortKey { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public UnitResult<Error> Update(Title title, Description? description, AccessType accessType)
    {
        UnitResult<Error> accessCheck = CollectionAccessPolicy.CanSetAccessType(accessType, CourseId);
        if (accessCheck.IsFailure)
            return accessCheck;

        Title = title;
        Description = description;
        AccessType = accessType;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Вызывается до удаления курса подборки — обнуляет <see cref="CourseId"/>,
    ///     синхронизируя in-memory aggregate с FK <c>SetNull</c> на уровне БД.
    ///     После #77 AccessType больше не даунгрейдится: orphan ENROLLED легитимен
    ///     и гейтится через платформенный <c>plan:all</c> (см. <c>ContentAccessTagBuilder</c>).
    ///     Caller обязан опубликовать <c>CollectionAccessChanged</c> с новым (пустым)
    ///     CourseId, чтобы sync-handler пересчитал Redis-теги.
    /// </summary>
    public void OnCourseDetached()
    {
        if (CourseId is null)
            return;

        CourseId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Публикует подборку. Инвариант «хотя бы один элемент» внешний: вызывающий
    ///     (handler) считает наличие items через <c>ICollectionItemsRepository</c>
    ///     и передаёт флаг. Так aggregate не тянет детей в память.
    /// </summary>
    public UnitResult<Error> Publish(bool hasAnyItem)
    {
        if (Status == PublicationStatus.PUBLISHED)
            return EducationErrors.InvalidCollectionStatusTransition(Status.ToString(), "PUBLISHED");

        if (!hasAnyItem)
            return EducationErrors.CannotPublishEmptyCollection();

        Status = PublicationStatus.PUBLISHED;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> SendToDraft()
    {
        if (Status != PublicationStatus.PUBLISHED)
            return EducationErrors.InvalidCollectionStatusTransition(Status.ToString(), "DRAFT");

        Status = PublicationStatus.DRAFT;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Archive()
    {
        if (Status != PublicationStatus.PUBLISHED)
            return EducationErrors.InvalidCollectionStatusTransition(Status.ToString(), "ARCHIVED");

        Status = PublicationStatus.ARCHIVED;
        UpdatedAt = DateTime.UtcNow;
        return UnitResult.Success<Error>();
    }

    public void Pin(SortKey sortKey)
    {
        IsPinned = true;
        PinnedSortKey = sortKey;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unpin()
    {
        IsPinned = false;
        PinnedSortKey = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AttachImage(Guid imageId, long bindingRevision = 0)
    {
        CoverImageId = imageId;
        CoverBindingRevision = bindingRevision;
        MediaVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DetachImage()
    {
        CoverImageId = null;
        CoverBindingRevision = 0;
        MediaVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Помечает aggregate изменённым (UpdatedAt). Вызывается из хендлеров, когда
    ///     секция/элемент создаётся или удаляется отдельно — сам aggregate своих детей
    ///     не знает, но логически «подборка обновлена».
    /// </summary>
    public void Touch() => UpdatedAt = DateTime.UtcNow;
}
