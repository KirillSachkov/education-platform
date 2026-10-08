using TagService.Domain.TagAliases;

namespace TagService.Domain.Tags;

/// <summary>
/// Тег, связанный с целевой сущностью / Tag associated with a target entity.
/// </summary>
public sealed class Tag
{
    private readonly List<TagAlias> _aliases = [];

    private Tag(TagId id, TagTitle title, TagSlug slug, Guid authorId)
    {
        Id = id;
        Title = title;
        Slug = slug;
        AuthorId = authorId;
        Kind = TagKind.CANON;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // EF Core
    private Tag()
    {
    }

    /// <summary>
    /// Создает новый тег / Creates a new tag.
    /// </summary>
    /// <param name="title">Заголовок тега / Tag title.</param>
    /// <param name="slug">Слаг тега / Tag slug.</param>
    /// <param name="authorId">Идентификатор автора / Author identifier.</param>
    /// <returns>Результат создания тега / Tag creation result.</returns>
    public static Result<Tag, Error> Create(TagTitle title, TagSlug slug, Guid authorId)
    {
        if (title is null)
            return GeneralErrors.ValueIsRequired("tag.title");

        if (slug is null)
            return GeneralErrors.ValueIsRequired("tag.slug");

        if (authorId == Guid.Empty)
            return GeneralErrors.ValueIsRequired("tag.authorId");

        return new Tag(TagId.Create(), title, slug, authorId);
    }

    /// <summary>
    /// Идентификатор тега / Tag identifier.
    /// </summary>
    public TagId Id { get; private set; } = null!;

    /// <summary>
    /// Идентификатор автора / Author identifier.
    /// </summary>
    public Guid AuthorId { get; private set; }

    /// <summary>
    /// Заголовок тега / Tag title.
    /// </summary>
    public TagTitle Title { get; private set; } = null!;

    /// <summary>
    /// Слаг тега / Tag slug.
    /// </summary>
    public TagSlug Slug { get; private set; } = null!;

    /// <summary>
    /// Вид тега / Tag kind.
    /// </summary>
    public TagKind Kind { get; private set; }

    /// <summary>
    /// Алиасы, назначенные этому каноническому тегу / Aliases assigned to this canonical tag.
    /// </summary>
    public IReadOnlyList<TagAlias> Aliases => _aliases;

    /// <summary>
    /// Дата и время создания тега / Tag creation date and time.
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Дата и время последнего обновления тэга / Tag last update date and time.
    /// </summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Обновляет заголовок и слаг тега / Updates the tag title and slug.
    /// </summary>
    /// <param name="title">Новый заголовок тега / New tag title.</param>
    /// <param name="slug">Новый слаг тега / New tag slug.</param>
    public void Update(TagTitle title, TagSlug slug)
    {
        Title = title;
        Slug = slug;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsAlias()
    {
        Kind = TagKind.ALIAS;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkAsRegular()
    {
        Kind = TagKind.CANON;
        UpdatedAt = DateTime.UtcNow;
    }
}
