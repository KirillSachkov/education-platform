using EducationContentService.Domain.ValueObjects;

namespace EducationContentService.Domain.Materials;

/// <summary>
///     Aggregate Root — унифицированный образовательный материал.
///     Объединяет старые Lesson и Article: поддерживает и текстовый контент,
///     и прикреплённое видео, разделяется на типы (<see cref="MaterialKind"/>) — статья,
///     видео-урок, заметка, стрим, best-practice.
/// </summary>
/// <remarks>
///     Инвариант публикации: перед переводом в <see cref="PublicationStatus.PUBLISHED"/>
///     у материала должен быть либо <see cref="Content"/>, либо <see cref="VideoId"/>
///     (пустой Draft опубликовать нельзя).
/// </remarks>
public sealed class Material
{
    /// <summary>
    ///     Создаёт новый материал в статусе <see cref="PublicationStatus.DRAFT"/>.
    /// </summary>
    public Material(
        Guid authorId,
        Title title,
        MaterialKind kind = MaterialKind.ARTICLE,
        AccessType accessType = AccessType.PUBLIC)
    {
        Id = Guid.CreateVersion7();
        AuthorId = authorId;
        Title = title;
        Kind = kind;
        AccessType = accessType;
        Status = PublicationStatus.DRAFT;
        Content = null;
        Description = null;
        ImageId = null;
        VideoId = null;

        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    // EF Core
    private Material()
    {
    }

    /// <summary>Идентификатор материала (UUID v7, отсортирован по времени).</summary>
    public Guid Id { get; }

    /// <summary>Идентификатор автора — владелец материала.</summary>
    public Guid AuthorId { get; }

    /// <summary>Заголовок материала.</summary>
    public Title Title { get; private set; } = null!;

    /// <summary>Текстовый контент в Markdown — может отсутствовать (например, у чистого видео).</summary>
    public MarkdownContent? Content { get; private set; }

    /// <summary>
    ///     Авторское описание материала в Markdown (полезные ссылки, связанные материалы, заметки).
    ///     Отдельно от <see cref="Content"/> (AI-конспект): редактируется автором вручную,
    ///     показывается на странице материала. Опционально.
    /// </summary>
    public MarkdownContent? Description { get; private set; }

    /// <summary>Вариант материала (статья / видео / заметка / стрим / best-practice).</summary>
    public MaterialKind Kind { get; private set; }

    /// <summary>Тип доступа — кто может видеть материал.</summary>
    public AccessType AccessType { get; private set; }

    /// <summary>Статус публикации.</summary>
    public PublicationStatus Status { get; private set; }

    /// <summary>Обложка материала (опционально).</summary>
    public ImageId? ImageId { get; private set; }

    /// <summary>Видеозапись, прикреплённая к материалу (опционально).</summary>
    public VideoId? VideoId { get; private set; }

    public long ImageBindingRevision { get; private set; }

    public long VideoBindingRevision { get; private set; }

    public long MediaVersion { get; private set; }

    /// <summary>
    ///     Квиз «Проверь себя», на который ссылается материал (опционально, #489).
    ///     Квиз — самостоятельный aggregate (<c>quizzes</c>); один квиз может
    ///     переиспользоваться несколькими материалами. Без FK-constraint'а —
    ///     консистентно с <see cref="VideoId"/>/<see cref="ImageId"/>; обнуление
    ///     при удалении квиза делает каскад <c>DeleteQuizHandler</c>.
    /// </summary>
    public Guid? QuizId { get; private set; }

    /// <summary>
    ///     Денормализованный список заголовков глав видео — синхронизируется из Kinescope
    ///     через <c>PUT /internal/videos/{id}/chapters/</c>.
    ///     Длина и порядок совпадают с <see cref="ChapterTimestamps"/>.
    /// </summary>
    public IReadOnlyList<string> ChapterTitles { get; private set; } = [];

    /// <summary>
    ///     Денормализованный список offset'ов глав видео в секундах — параллельный массив
    ///     к <see cref="ChapterTitles"/>: <c>ChapterTimestamps[i]</c> — offset для
    ///     <c>ChapterTitles[i]</c>. Используется для перехода к главе видео.
    /// </summary>
    public IReadOnlyList<int> ChapterTimestamps { get; private set; } = [];

    /// <summary>Время создания (UTC).</summary>
    public DateTime CreatedAt { get; }

    /// <summary>Время последнего изменения (UTC).</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Время первой публикации (UTC). Устанавливается один раз при первом Publish().</summary>
    public DateTime? PublishedAt { get; private set; }

    public UnitResult<Error> ValidateProspectivePayload(MarkdownContent? content, Guid? videoId)
    {
        if (Status == PublicationStatus.PUBLISHED && content is null && videoId is null)
            return EducationErrors.CannotPublishMaterialWithoutContent();

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Обновляет основные поля материала.
    ///     Проверяет инвариант «AccessType ↔ привязка к курсу» (<see cref="MaterialAccessPolicy"/>).
    /// </summary>
    /// <param name="title">Новый заголовок.</param>
    /// <param name="content">Новое содержимое (nullable).</param>
    /// <param name="kind">Новый тип материала.</param>
    /// <param name="accessType">Новый уровень доступа.</param>
    /// <param name="boundCourseCount">
    ///     Количество привязок материала к курсам (<c>course_materials</c>).
    ///     Используется для валидации AccessType ∈ {FREE, ENROLLED}.
    /// </param>
    /// <param name="description">Новое авторское описание (nullable).</param>
    public UnitResult<Error> Update(
        Title title,
        MarkdownContent? content,
        MaterialKind kind,
        AccessType accessType,
        int boundCourseCount,
        MarkdownContent? description)
    {
        UnitResult<Error> accessCheck = MaterialAccessPolicy.CanSetAccessType(accessType, boundCourseCount);
        if (accessCheck.IsFailure)
            return accessCheck;

        Title = title;
        Content = content;
        Description = description;
        Kind = kind;
        AccessType = accessType;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Устанавливает контент материала без повторной валидации <see cref="MaterialAccessPolicy"/>.
    ///     Использовать только в use-cases, где политика уже проверена (например, в <c>Create</c>
    ///     сразу после конструктора, чтобы избежать двойного запуска policy).
    /// </summary>
    public void SetContent(MarkdownContent? content)
    {
        Content = content;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Устанавливает авторское описание материала без повторной валидации
    ///     <see cref="MaterialAccessPolicy"/>. Использовать только в use-cases, где политика уже
    ///     проверена (например, в <c>Create</c> сразу после конструктора).
    /// </summary>
    public void SetDescription(MarkdownContent? description)
    {
        Description = description;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Сменить только уровень доступа. Используется в bulk-операциях (например,
    ///     <c>BulkSetCollectionItemsAccessType</c>), когда title/content/kind не меняются и
    ///     дёргать полный <see cref="Update"/> неуместно. Возвращает <c>true</c> только если
    ///     <see cref="AccessType"/> действительно сменился — caller использует это для skip-логики
    ///     и подсчёта <c>updatedCount</c>/<c>skippedCount</c>.
    /// </summary>
    public bool ChangeAccessType(AccessType accessType)
    {
        if (AccessType == accessType)
            return false;

        AccessType = accessType;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    ///     Переводит материал в <see cref="PublicationStatus.PUBLISHED"/>.
    ///     Разрешено из <see cref="PublicationStatus.DRAFT"/> или <see cref="PublicationStatus.ARCHIVED"/>.
    ///     Требует наличия <see cref="Content"/> или <see cref="VideoId"/>.
    /// </summary>
    public UnitResult<Error> Publish()
    {
        if (Status != PublicationStatus.DRAFT && Status != PublicationStatus.ARCHIVED)
            return EducationErrors.InvalidMaterialStatusTransition(
                Status.ToString(), nameof(PublicationStatus.PUBLISHED));

        if (Content is null && VideoId is null)
            return EducationErrors.CannotPublishMaterialWithoutContent();

        PublishedAt ??= DateTime.UtcNow;
        Status = PublicationStatus.PUBLISHED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Переводит материал обратно в <see cref="PublicationStatus.DRAFT"/>.
    ///     Разрешено только из <see cref="PublicationStatus.PUBLISHED"/>.
    /// </summary>
    public UnitResult<Error> SendToDraft()
    {
        if (Status != PublicationStatus.PUBLISHED)
            return EducationErrors.InvalidMaterialStatusTransition(
                Status.ToString(), nameof(PublicationStatus.DRAFT));

        Status = PublicationStatus.DRAFT;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    /// <summary>
    ///     Архивирует материал.
    ///     Разрешено только из <see cref="PublicationStatus.PUBLISHED"/>.
    /// </summary>
    public UnitResult<Error> Archive()
    {
        if (Status != PublicationStatus.PUBLISHED)
            return EducationErrors.InvalidMaterialStatusTransition(
                Status.ToString(), nameof(PublicationStatus.ARCHIVED));

        Status = PublicationStatus.ARCHIVED;
        UpdatedAt = DateTime.UtcNow;

        return UnitResult.Success<Error>();
    }

    /// <summary>Прикрепляет видеозапись к материалу.</summary>
    public void AttachVideo(VideoId videoId, long bindingRevision = 0)
    {
        VideoId = videoId;
        VideoBindingRevision = bindingRevision;
        MediaVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Отвязывает видеозапись от материала.</summary>
    public void DetachVideo()
    {
        VideoId = null;
        VideoBindingRevision = 0;
        MediaVersion++;
        ChapterTitles = [];
        ChapterTimestamps = [];
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    ///     Заменяет главы видео (parallel arrays: titles[i] ↔ timestamps[i] в секундах).
    ///     Принимает уже нормализованный список — валидация (trim, non-empty) лежит на
    ///     caller'е (handler'е). Идемпотентно. Гарантирует invariant на длины:
    ///     <c>ChapterTitles.Count == ChapterTimestamps.Count</c> — фронт по индексу
    ///     совпавшей главы из highlight'а резолвит timestamp; рассинхрон длин даёт
    ///     out-of-bounds на стороне фронта.
    /// </summary>
    public void UpdateChapters(IReadOnlyList<string> chapterTitles, IReadOnlyList<int> chapterTimestamps)
    {
        if (chapterTitles.Count != chapterTimestamps.Count)
        {
            throw new ArgumentException(
                $"chapter_titles and chapter_timestamps must have equal length: " +
                $"got {chapterTitles.Count} titles, {chapterTimestamps.Count} timestamps",
                nameof(chapterTimestamps));
        }

        ChapterTitles = chapterTitles;
        ChapterTimestamps = chapterTimestamps;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Привязывает квиз «Проверь себя» к материалу (идемпотентно).</summary>
    public void AttachQuiz(Guid quizId)
    {
        QuizId = quizId;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Отвязывает квиз от материала (сам квиз продолжает жить — он standalone).</summary>
    public void DetachQuiz()
    {
        QuizId = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Прикрепляет обложку к материалу.</summary>
    public void AttachImage(ImageId imageId, long bindingRevision = 0)
    {
        ImageId = imageId;
        ImageBindingRevision = bindingRevision;
        MediaVersion++;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Отвязывает обложку от материала.</summary>
    public void DetachImage()
    {
        ImageId = null;
        ImageBindingRevision = 0;
        MediaVersion++;
        UpdatedAt = DateTime.UtcNow;
    }
}
