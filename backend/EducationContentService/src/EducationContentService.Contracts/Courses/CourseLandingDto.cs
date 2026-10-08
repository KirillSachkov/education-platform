namespace EducationContentService.Contracts.Courses;

/// <summary>
///     Полная информация о курсе для публичного лендинга.
///     <para>
///     ВНИМАНИЕ (#569): здесь намеренно НЕТ author-credit полей. Этот DTO питает pricing
///     «что входит» + SEO JSON-LD, где байлайн автора не показывается. Author-credit живёт
///     на <see cref="CourseCatalogDto"/> (карточки), <see cref="CourseDetailDto"/> и
///     <see cref="CourseCurriculumDto"/> (страница курса). Если понадобится показать
///     <c>AuthorCredit</c> с лендинга — СНАЧАЛА обогати этот DTO через IAuthorLookupClient
///     в GetCourseLanding, иначе фронт молча отрендерит null.
///     </para>
/// </summary>
/// <param name="HasFreeContent">
///     true — у курса есть хотя бы один опубликованный урок или задание
///     с AccessType=FREE. Frontend использует это, чтобы показывать кнопку
///     «Попробовать бесплатно».
/// </param>
public sealed record CourseLandingDto(
    Guid Id,
    Guid AuthorId,
    string Slug,
    string Title,
    string Description,
    string Status,
    string Kind,
    Guid? ImageId,
    string? ImageUrl,
    Guid? VideoId,
    CourseLandingStatsDto Stats,
    bool HasFreeContent,
    bool IsNew,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<CurriculumSectionDto> Sections);

/// <summary>
///     Авто-подсчитываемая статистика курса.
/// </summary>
/// <param name="QuizCount">
///     DISTINCT PUBLISHED-квизы из module_items модулей курса (#551) —
///     дефолт 0, чтобы не ломать существующие вызовы конструктора.
/// </param>
public sealed record CourseLandingStatsDto(
    int ModuleCount,
    int LessonCount,
    int IssueCount,
    int QuizCount = 0);
