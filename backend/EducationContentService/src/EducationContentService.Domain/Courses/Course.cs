using EducationContentService.Domain.ValueObjects;
using Ordering;

namespace EducationContentService.Domain.Courses;

/// <summary>
///     Aggregate Root — курс как коммерческий продукт.
///     Объединяет Module и Project через CourseItem.
/// </summary>
public sealed class Course : IOrderedItem
{
    private const int MAX_LANDING_ITEMS = 25;
    private const int MAX_LANDING_ITEM_LENGTH = 200;

    public Course(
        Guid authorId,
        Title title,
        Description description,
        CourseSlug slug,
        SortKey sortKey,
        CourseKind kind = CourseKind.COURSE,
        bool showInFullAccess = true,
        bool isCatalogListed = true)
    {
        Id = Guid.CreateVersion7();
        AuthorId = authorId;
        Title = title;
        Description = description;
        Status = PublicationStatus.DRAFT;
        ImageId = null;
        VideoId = null;
        Slug = slug;
        IsNew = false;
        Kind = kind;
        ShowInFullAccess = showInFullAccess;
        IsCatalogListed = isCatalogListed;
        SortKey = sortKey;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // EF Core
    private Course()
    {
    }

    public Guid Id { get; }

    public Guid AuthorId { get; private set; }

    public Title Title { get; private set; } = null!;

    public Description Description { get; private set; } = null!;

    public PublicationStatus Status { get; private set; }

    public ImageId? ImageId { get; private set; }

    public VideoId? VideoId { get; private set; }

    public long ImageBindingRevision { get; private set; }

    public long VideoBindingRevision { get; private set; }

    public long MediaVersion { get; private set; }

    public long AssetOwnershipRevision { get; private set; }

    public Guid? GettingStartedModuleId { get; private set; }

    public CourseSlug Slug { get; private set; } = null!;

    public bool IsNew { get; private set; }

    /// <summary>
    ///     Тип курса (см. <see cref="CourseKind"/>). Задаётся при создании, корректируется
    ///     автором/модератором через <see cref="ChangeKind"/> (PATCH /courses/{id}) — нужно,
    ///     чтобы исправить миску атегоризацию (например, проект-марафон, заведённый как интенсив).
    ///     Интенсив/марафон отличаются от курса только UX-правилами (нет заданий в модулях,
    ///     отдельная секция каталога). Access-модель идентична — материалы шарятся через те же
    ///     plan-tags, поэтому смена типа не трогает Redis-теги и не требует event'а сверх
    ///     общего <c>course.updated</c>. Guard на issue-инвариант — в use-case'е (см. ChangeKind).
    /// </summary>
    public CourseKind Kind { get; private set; }

    /// <summary>
    ///     Display-флаг: показывать ли курс в showcase-карточке «Полный доступ» на <c>/pricing</c>.
    ///     На доступ НЕ влияет (FULL_ALL-план покрывает весь контент независимо от флага).
    ///     Default — <c>true</c>; автор снимает у интенсивов/материалов, дублирующихся внутри курсов,
    ///     чтобы не раздувать витрину «что входит».
    /// </summary>
    public bool ShowInFullAccess { get; private set; }

    /// <summary>
    ///     Catalog-visibility gate (issue #569, model A co-author). PUBLISHED-курс
    ///     попадает в публичный каталог / home / by-author портфолио только когда
    ///     <c>true</c>. Default при создании — <c>true</c> для админа, <c>false</c> для
    ///     обычного автора (его курс ждёт одобрения модератором витрины). На сам
    ///     доступ к контенту НЕ влияет — это только про показ карточки в каталоге.
    ///     Автор всегда видит свой курс в <c>/courses/my</c> независимо от флага.
    ///     Standalone-материалы базы знаний под этот гейт НЕ попадают.
    /// </summary>
    public bool IsCatalogListed { get; private set; }

    /// <summary>
    ///     Author-authored landing copy «Чему вы научитесь» — результаты обучения.
    ///     Опционально, рендерится на лендинге курса для не-купивших. Денорм <c>text[]</c>.
    /// </summary>
    public IReadOnlyList<string> LearningOutcomes { get; private set; } = [];

    /// <summary>«Для кого этот курс» — целевая аудитория. Опционально. Денорм <c>text[]</c>.</summary>
    public IReadOnlyList<string> TargetAudience { get; private set; } = [];

    /// <summary>«Что нужно знать заранее» — пререквизиты. Опционально. Денорм <c>text[]</c>.</summary>
    public IReadOnlyList<string> Prerequisites { get; private set; } = [];

    /// <summary>
    ///     Author-defined display order. Scope is per-author (`AuthorId`), uses fractional
    ///     indexing — appended on Create, mutated via <see cref="UpdateSortKey"/> on drag-n-drop.
    ///     Used by catalog / by-author / admin queries to sort courses.
    /// </summary>
    public SortKey SortKey { get; private set; } = null!;

    public DateTime CreatedAt { get; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>Момент первой публикации (зеркало Material.PublishedAt). null — ещё не публиковался.</summary>
    public DateTime? PublishedAt { get; private set; }

    public void UpdateSortKey(SortKey sortKey)
    {
        SortKey = sortKey;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetTitle(Title title)
    {
        Title = title;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetDescription(Description description)
    {
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetLearningOutcomes(IReadOnlyList<string> outcomes)
    {
        LearningOutcomes = NormalizeLandingList(outcomes);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetTargetAudience(IReadOnlyList<string> audience)
    {
        TargetAudience = NormalizeLandingList(audience);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetPrerequisites(IReadOnlyList<string> prerequisites)
    {
        Prerequisites = NormalizeLandingList(prerequisites);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Trims, drops blanks, caps item length and count. Editorial landing lists —
    ///     author-only input, so the cap is a defensive guard rather than a hard validation.
    /// </summary>
    private static IReadOnlyList<string> NormalizeLandingList(IReadOnlyList<string> items)
    {
        if (items is null || items.Count == 0)
            return [];

        return items
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .Select(s => s.Length > MAX_LANDING_ITEM_LENGTH ? s[..MAX_LANDING_ITEM_LENGTH] : s)
            .Take(MAX_LANDING_ITEMS)
            .ToList();
    }

    public void SetGettingStartedModule(Guid? gettingStartedModuleId)
    {
        GettingStartedModuleId = gettingStartedModuleId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetSlug(CourseSlug slug)
    {
        Slug = slug;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Корректирует тип курса. Issue-инвариант (INTENSIVE/MARATHON не держат заданий)
    ///     проверяет use-case до вызова — у aggregate нет своих issues в памяти.
    /// </summary>
    public void ChangeKind(CourseKind kind)
    {
        Kind = kind;
        UpdatedAt = DateTime.UtcNow;
    }

    public UnitResult<Error> Publish()
    {
        if (Status != PublicationStatus.DRAFT)
            return EducationErrors.InvalidStatusTransition(Status.ToString(), nameof(PublicationStatus.PUBLISHED));

        Status = PublicationStatus.PUBLISHED;
        PublishedAt ??= DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Archive()
    {
        if (Status != PublicationStatus.PUBLISHED)
            return EducationErrors.InvalidStatusTransition(Status.ToString(), nameof(PublicationStatus.ARCHIVED));

        Status = PublicationStatus.ARCHIVED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    public UnitResult<Error> Restore()
    {
        if (Status != PublicationStatus.ARCHIVED)
            return EducationErrors.InvalidStatusTransition(Status.ToString(), nameof(PublicationStatus.PUBLISHED));

        Status = PublicationStatus.PUBLISHED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    public void SetIsNew(bool isNew)
    {
        IsNew = isNew;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetShowInFullAccess(bool showInFullAccess)
    {
        ShowInFullAccess = showInFullAccess;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Admin/moderator approval toggle for catalog visibility (issue #569).
    ///     Display-only — не трогает access-tag'и и не публикует integration-event'ов.
    /// </summary>
    public void SetCatalogListed(bool listed)
    {
        IsCatalogListed = listed;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Admin/moderator ownership transfer (issue #587). Reassigns the course root to a
    ///     new author. The course's child content (modules, projects, issues, exclusively
    ///     owned materials/quizzes, course-level collections) is reassigned separately by
    ///     the use-case via bulk SQL. Ownership-only change: it does NOT touch access tags,
    ///     so existing students keep access through the course-plan (which is author-agnostic).
    /// </summary>
    public void ReassignAuthor(Guid newAuthorId, long assetOwnershipRevision)
    {
        AuthorId = newAuthorId;
        AssetOwnershipRevision = assetOwnershipRevision;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AttachImage(ImageId imageId, long bindingRevision = 0)
    {
        ImageId = imageId;
        ImageBindingRevision = bindingRevision;
        MediaVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DetachImage()
    {
        ImageId = null;
        ImageBindingRevision = 0;
        MediaVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AttachVideo(VideoId videoId, long bindingRevision = 0)
    {
        VideoId = videoId;
        VideoBindingRevision = bindingRevision;
        MediaVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DetachVideo()
    {
        VideoId = null;
        VideoBindingRevision = 0;
        MediaVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ClearGettingStartedModuleIfMatches(Guid moduleId)
    {
        if (GettingStartedModuleId == moduleId)
        {
            GettingStartedModuleId = null;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
