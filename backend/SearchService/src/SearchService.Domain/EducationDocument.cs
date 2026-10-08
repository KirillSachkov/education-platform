using System.Text.Json.Serialization;
using Common;

namespace SearchService.Domain;



/// <summary>
/// Представляет денормализованную модель данных (Read Model), используемую для полнотекстового поиска.
/// </summary>
public sealed class EducationDocument
{
    [JsonConstructor]
    private EducationDocument()
    {
    }

    private EducationDocument(
        Guid entityId,
        EntityType entityType,
        string title,
        string? description,
        Guid? imageId,
        IReadOnlyList<string>? requiredAccessTags,
        Guid? courseId,
        string? courseSlug,
        string? courseTitle,
        CourseAccessType? courseAccessType,
        Guid? authorId,
        Guid? projectId,
        string? projectTitle,
        Guid? moduleId,
        string? moduleTitle,
        IReadOnlyList<Guid>? tagIds,
        IReadOnlyList<string>? tagTitles,
        DateTime updatedAt,
        bool isDeleted,
        string reindexGeneration,
        string? materialKind = null,
        string? content = null,
        Guid? videoId = null,
        IReadOnlyList<string>? chapterTitles = null,
        IReadOnlyList<int>? chapterTimestamps = null)
    {
        Id = CreateId(entityType, entityId);
        EntityType = entityType;
        EntityId = entityId;
        Title = title;
        Description = description;
        ImageId = imageId;
        RequiredAccessTags = requiredAccessTags?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        CourseId = courseId;
        CourseSlug = courseSlug;
        CourseTitle = courseTitle;
        CourseAccessType = courseAccessType;
        AuthorId = authorId;
        ProjectId = projectId;
        ProjectTitle = projectTitle;
        ModuleId = moduleId;
        ModuleTitle = moduleTitle;
        TagIds = tagIds ?? [];
        TagTitles = tagTitles ?? [];
        UpdatedAtTicks = updatedAt.ToUniversalTime().Ticks;
        IsDeleted = isDeleted;
        ReindexGeneration = reindexGeneration;
        MaterialKind = materialKind;
        Content = content;
        VideoId = videoId;
        ChapterTitles = chapterTitles?.ToArray() ?? [];
        ChapterTimestamps = chapterTimestamps?.ToArray() ?? [];
    }

    public const string LIVE_REINDEX_GENERATION = "live";

    /// <summary>
    /// Идентификатор документа в поисковом индексе.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Идентификатор целевой сущности.
    /// </summary>
    [JsonPropertyName("entity_id")]
    public Guid EntityId { get; init; }

    /// <summary>
    /// Тип целевой сущности.
    /// </summary>
    [JsonPropertyName("entity_type")]
    public EntityType EntityType { get; init; }

    /// <summary>
    /// Наименование сущности.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Краткое описание сущности.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Идентификатор preview-изображения документа.
    /// </summary>
    [JsonPropertyName("image_id")]
    public Guid? ImageId { get; init; }

    /// <summary>
    /// Теги доступа, которые дают право видеть документ.
    /// </summary>
    [JsonPropertyName("required_access_tags")]
    public IReadOnlyList<string> RequiredAccessTags { get; init; } = [];

    /// <summary>
    /// Курс.
    /// </summary>
    [JsonPropertyName("course_id")]
    public Guid? CourseId { get; init; }

    /// <summary>
    /// Slug курса для навигации во frontend.
    /// </summary>
    [JsonPropertyName("course_slug")]
    public string? CourseSlug { get; init; }

    /// <summary>
    /// Наименование курса.
    /// </summary>
    [JsonPropertyName("course_title")]
    public string? CourseTitle { get; init; }

    /// <summary>
    /// Тип доступа к курсу.
    /// </summary>
    [JsonPropertyName("course_access_type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CourseAccessType? CourseAccessType { get; init; }

    /// <summary>
    /// Идентификатор автора документа. Для материалов/курсов/модулей/проектов —
    /// владелец курса (или автор space-level материала). Позволяет ограничить поиск
    /// пространством автора на /@slug/ страницах.
    /// </summary>
    [JsonPropertyName("author_id")]
    public Guid? AuthorId { get; init; }

    /// <summary>
    /// Идентификатор проекта.
    /// </summary>
    [JsonPropertyName("project_id")]
    public Guid? ProjectId { get; init; }

    /// <summary>
    /// Наименование проекта.
    /// </summary>
    [JsonPropertyName("project_title")]
    public string? ProjectTitle { get; init; }

    /// <summary>
    /// Идентификатор модуля.
    /// </summary>
    [JsonPropertyName("module_id")]
    public Guid? ModuleId { get; init; }

    /// <summary>
    /// Наименование модуля.
    /// </summary>
    [JsonPropertyName("module_title")]
    public string? ModuleTitle { get; init; }

    /// <summary>
    /// Идентификаторы тегов, ассоциированных с сущностью.
    /// </summary>
    [JsonPropertyName("tag_ids")]
    public IReadOnlyList<Guid> TagIds { get; init; } = [];

    /// <summary>
    /// Наименования тегов, ассоциированных с сущностью.
    /// </summary>
    [JsonPropertyName("tag_titles")]
    public IReadOnlyList<string> TagTitles { get; init; } = [];

    /// <summary>
    /// Время последнего обновления документа (UTC ticks).
    /// </summary>
    [JsonPropertyName("updated_at_ticks")]
    public long UpdatedAtTicks { get; init; }

    /// <summary>
    /// Флаг, указывающий, удален ли документ.
    /// </summary>
    [JsonPropertyName("is_deleted")]
    public bool IsDeleted { get; init; }

    /// <summary>
    /// Маркер поколения partial reindex для безопасной очистки stale-документов.
    /// </summary>
    [JsonPropertyName("reindex_generation")]
    public string ReindexGeneration { get; init; } = LIVE_REINDEX_GENERATION;

    /// <summary>
    /// Kind материала (ARTICLE / VIDEO / NOTE / STREAM). Только для EntityType.Material; для
    /// остальных типов — null. Используется фронтом для показа правильной иконки/превью.
    /// </summary>
    [JsonPropertyName("material_kind")]
    public string? MaterialKind { get; init; }

    /// <summary>
    /// Полный markdown-текст материала (до 200 КБ). Индексируется для полнотекстового поиска
    /// с морфологией (locale=ru), но <b>не отдаётся в ответе</b> — запрос SearchService
    /// передаёт `exclude_fields=content` и возвращает только сниппет через highlight.
    /// Наличие поля в Typesense не равно публичному доступу к телу материала: лок-резолвер
    /// определяет, какие документы показывать с замком, а сниппет ограничен ~30 словами
    /// вокруг совпадения (как preview-строка в feed-endpoint'ах).
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>
    /// Kinescope VideoId материала (для kind=VIDEO). Нужен поиску, чтобы подгрузить
    /// реальный thumbnail через FileService batch вместо gradient-заглушки.
    /// Индексируется без index:true — только как lookup payload.
    /// </summary>
    [JsonPropertyName("video_id")]
    public Guid? VideoId { get; init; }

    /// <summary>
    /// Заголовки глав видео (Kinescope chapters), денормализованные из Material.ChapterTitles.
    /// Индексируется как массив строк для полнотекстового поиска — пользователь может найти
    /// урок по фразе из заголовка главы. Полная транскрипция в Typesense НЕ хранится —
    /// только конспект (<see cref="Content"/>) и эти заголовки + базовые поля.
    /// </summary>
    [JsonPropertyName("chapter_titles")]
    public IReadOnlyList<string> ChapterTitles { get; init; } = [];

    /// <summary>
    /// Offset'ы глав в секундах (parallel array с <see cref="ChapterTitles"/>:
    /// <c>ChapterTimestamps[i]</c> — offset для <c>ChapterTitles[i]</c>). Payload-поле,
    /// не индексируется (поиск по числам не нужен) — нужен фронту чтобы по индексу
    /// совпавшей главы из <c>highlight.indices</c> собрать deep-link <c>?t=&lt;seconds&gt;</c>.
    /// </summary>
    [JsonPropertyName("chapter_timestamps")]
    public IReadOnlyList<int> ChapterTimestamps { get; init; } = [];

    public static string CreateCourseId(Guid entityId) => CreateId(EntityType.Course, entityId);

    public static string CreateProjectId(Guid entityId) => CreateId(EntityType.Project, entityId);

    public static string CreateMaterialId(Guid entityId) => CreateId(EntityType.Material, entityId);

    public static string CreateIssueId(Guid entityId) => CreateId(EntityType.Issue, entityId);

    public static string CreateModuleId(Guid entityId) => CreateId(EntityType.Module, entityId);

    public static string CreateCollectionId(Guid entityId) => CreateId(EntityType.Collection, entityId);

    public static EducationDocument CreateCourse(
        Guid courseId,
        string title,
        string? description,
        DateTime updatedAt,
        IReadOnlyList<string>? requiredAccessTags = null,
        CourseAccessType? accessType = null,
        IReadOnlyList<Guid>? tagIds = null,
        IReadOnlyList<string>? tagTitles = null,
        bool isDeleted = false,
        string reindexGeneration = LIVE_REINDEX_GENERATION,
        string? courseSlug = null,
        Guid? authorId = null) =>
        new (
            entityId: courseId,
            entityType: EntityType.Course,
            title: title,
            description: description,
            imageId: null,
            requiredAccessTags: requiredAccessTags,
            courseId: courseId,
            courseSlug: courseSlug,
            courseTitle: title,
            courseAccessType: accessType,
            authorId: authorId,
            projectId: null,
            projectTitle: null,
            moduleId: null,
            moduleTitle: null,
            tagIds: tagIds,
            tagTitles: tagTitles,
            updatedAt: updatedAt,
            isDeleted: isDeleted,
            reindexGeneration: reindexGeneration);

    public static EducationDocument CreateProject(
        Guid projectId,
        string title,
        string? description,
        DateTime updatedAt,
        IReadOnlyList<string>? requiredAccessTags = null,
        Guid? courseId = null,
        string? courseTitle = null,
        CourseAccessType? courseAccessType = null,
        IReadOnlyList<Guid>? tagIds = null,
        IReadOnlyList<string>? tagTitles = null,
        bool isDeleted = false,
        string reindexGeneration = LIVE_REINDEX_GENERATION,
        string? courseSlug = null,
        Guid? authorId = null) =>
        new (
            entityId: projectId,
            entityType: EntityType.Project,
            title: title,
            description: description,
            imageId: null,
            requiredAccessTags: requiredAccessTags,
            courseId: courseId,
            courseSlug: courseSlug,
            courseTitle: courseTitle,
            courseAccessType: courseAccessType,
            authorId: authorId,
            projectId: projectId,
            projectTitle: title,
            moduleId: null,
            moduleTitle: null,
            tagIds: tagIds,
            tagTitles: tagTitles,
            updatedAt: updatedAt,
            isDeleted: isDeleted,
            reindexGeneration: reindexGeneration);

    public static EducationDocument CreateModule(
        Guid moduleId,
        string title,
        string? description,
        DateTime updatedAt,
        IReadOnlyList<string>? requiredAccessTags = null,
        Guid? courseId = null,
        string? courseTitle = null,
        CourseAccessType? courseAccessType = null,
        IReadOnlyList<Guid>? tagIds = null,
        IReadOnlyList<string>? tagTitles = null,
        bool isDeleted = false,
        string reindexGeneration = LIVE_REINDEX_GENERATION,
        string? courseSlug = null,
        Guid? authorId = null) =>
        new (
            entityId: moduleId,
            entityType: EntityType.Module,
            title: title,
            description: description,
            imageId: null,
            requiredAccessTags: requiredAccessTags,
            courseId: courseId,
            courseSlug: courseSlug,
            courseTitle: courseTitle,
            courseAccessType: courseAccessType,
            authorId: authorId,
            projectId: null,
            projectTitle: null,
            moduleId: moduleId,
            moduleTitle: title,
            tagIds: tagIds,
            tagTitles: tagTitles,
            updatedAt: updatedAt,
            isDeleted: isDeleted,
            reindexGeneration: reindexGeneration);

    public static EducationDocument CreateIssue(
        Guid issueId,
        string title,
        DateTime updatedAt,
        IReadOnlyList<string>? requiredAccessTags = null,
        Guid? courseId = null,
        string? courseTitle = null,
        CourseAccessType? courseAccessType = null,
        Guid? projectId = null,
        string? projectTitle = null,
        Guid? moduleId = null,
        string? moduleTitle = null,
        IReadOnlyList<Guid>? tagIds = null,
        IReadOnlyList<string>? tagTitles = null,
        bool isDeleted = false,
        string reindexGeneration = LIVE_REINDEX_GENERATION,
        string? courseSlug = null,
        Guid? authorId = null) =>
        new (
            entityId: issueId,
            entityType: EntityType.Issue,
            title: title,
            description: null,
            imageId: null,
            requiredAccessTags: requiredAccessTags,
            courseId: courseId,
            courseSlug: courseSlug,
            courseTitle: courseTitle,
            courseAccessType: courseAccessType,
            authorId: authorId,
            projectId: projectId,
            projectTitle: projectTitle,
            moduleId: moduleId,
            moduleTitle: moduleTitle,
            tagIds: tagIds,
            tagTitles: tagTitles,
            updatedAt: updatedAt,
            isDeleted: isDeleted,
            reindexGeneration: reindexGeneration);

    public static EducationDocument CreateCollection(
        Guid collectionId,
        string title,
        string? description,
        DateTime updatedAt,
        Guid? imageId = null,
        IReadOnlyList<string>? requiredAccessTags = null,
        Guid? courseId = null,
        string? courseTitle = null,
        CourseAccessType? courseAccessType = null,
        IReadOnlyList<Guid>? tagIds = null,
        IReadOnlyList<string>? tagTitles = null,
        bool isDeleted = false,
        string reindexGeneration = LIVE_REINDEX_GENERATION,
        string? courseSlug = null,
        Guid? authorId = null) =>
        new (
            entityId: collectionId,
            entityType: EntityType.Collection,
            title: title,
            description: description,
            imageId: imageId,
            requiredAccessTags: requiredAccessTags,
            courseId: courseId,
            courseSlug: courseSlug,
            courseTitle: courseTitle,
            courseAccessType: courseAccessType,
            authorId: authorId,
            projectId: null,
            projectTitle: null,
            moduleId: null,
            moduleTitle: null,
            tagIds: tagIds,
            tagTitles: tagTitles,
            updatedAt: updatedAt,
            isDeleted: isDeleted,
            reindexGeneration: reindexGeneration);

    public static EducationDocument CreateMaterial(
        Guid materialId,
        string title,
        DateTime updatedAt,
        Guid? imageId = null,
        IReadOnlyList<string>? requiredAccessTags = null,
        Guid? courseId = null,
        string? courseTitle = null,
        CourseAccessType? courseAccessType = null,
        Guid? moduleId = null,
        string? moduleTitle = null,
        IReadOnlyList<Guid>? tagIds = null,
        IReadOnlyList<string>? tagTitles = null,
        bool isDeleted = false,
        string reindexGeneration = LIVE_REINDEX_GENERATION,
        string? courseSlug = null,
        Guid? authorId = null,
        string? materialKind = null,
        string? content = null,
        Guid? videoId = null,
        IReadOnlyList<string>? chapterTitles = null,
        IReadOnlyList<int>? chapterTimestamps = null) =>
        new (
            entityId: materialId,
            entityType: EntityType.Material,
            title: title,
            description: null,
            imageId: imageId,
            requiredAccessTags: requiredAccessTags,
            courseId: courseId,
            courseSlug: courseSlug,
            courseTitle: courseTitle,
            courseAccessType: courseAccessType,
            authorId: authorId,
            projectId: null,
            projectTitle: null,
            moduleId: moduleId,
            moduleTitle: moduleTitle,
            tagIds: tagIds,
            tagTitles: tagTitles,
            updatedAt: updatedAt,
            isDeleted: isDeleted,
            reindexGeneration: reindexGeneration,
            materialKind: materialKind,
            content: content,
            videoId: videoId,
            chapterTitles: chapterTitles,
            chapterTimestamps: chapterTimestamps);

    public static EducationDocument CreatePending(
        EntityType entityType,
        Guid entityId,
        IReadOnlyList<Guid>? tagIds = null,
        IReadOnlyList<string>? tagTitles = null,
        DateTime? updatedAt = null) =>
        new (
            entityId: entityId,
            entityType: entityType,
            title: string.Empty,
            description: null,
            imageId: null,
            requiredAccessTags: [],
            courseId: null,
            courseSlug: null,
            courseTitle: null,
            courseAccessType: null,
            authorId: null,
            projectId: null,
            projectTitle: null,
            moduleId: null,
            moduleTitle: null,
            tagIds: tagIds,
            tagTitles: tagTitles,
            updatedAt: updatedAt ?? DateTime.UtcNow,
            isDeleted: true,
            reindexGeneration: LIVE_REINDEX_GENERATION);

    public static string CreateId(EntityType entityType, Guid entityId) => $"{entityType}:{entityId:D}";

}
