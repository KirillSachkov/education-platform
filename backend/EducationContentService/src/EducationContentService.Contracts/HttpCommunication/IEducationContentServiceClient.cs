using EducationContentService.Contracts.Courses;
using EducationContentService.Contracts.Digest;
using EducationContentService.Contracts.Issues;
using EducationContentService.Contracts.Materials;
using EducationContentService.Contracts.Modules;
using EducationContentService.Contracts.Ownership;
using EducationContentService.Contracts.ProgressLookup;
using EducationContentService.Contracts.Projects;
using EducationContentService.Contracts.Quizzes;
using EducationContentService.Contracts.SearchLookup;

namespace EducationContentService.Contracts.HttpCommunication;

/// <summary>
///     HTTP-клиент для межсервисного взаимодействия с EducationContentService.
/// </summary>
public interface IEducationContentServiceClient
{
    Task<Result<CourseDetailDto, Error>> GetCourseDetailAsync(Guid courseId, CancellationToken cancellationToken);

    Task<Result<ModuleDetailDto, Error>> GetModuleDetailAsync(Guid moduleId, CancellationToken cancellationToken);

    Task<Result<ProjectDetailDto, Error>> GetProjectDetailAsync(Guid projectId, CancellationToken cancellationToken);

    Task<Result<IssueDetailDto, Error>> GetDetailIssueByIdAsync(Guid issueId, CancellationToken cancellationToken);

    Task<Result<CourseSearchLookupDto, Error>> GetCourseSearchLookupAsync(Guid courseId, CancellationToken cancellationToken);

    Task<Result<ModuleSearchLookupDto, Error>> GetModuleSearchLookupAsync(Guid moduleId, CancellationToken cancellationToken);

    Task<Result<ProjectSearchLookupDto, Error>> GetProjectSearchLookupAsync(Guid projectId, CancellationToken cancellationToken);

    Task<Result<MaterialSearchLookupDto, Error>> GetMaterialSearchLookupAsync(Guid materialId, CancellationToken cancellationToken);

    Task<Result<IssueSearchLookupDto, Error>> GetIssueSearchLookupAsync(Guid issueId, CancellationToken cancellationToken);

    Task<Result<CollectionSearchLookupDto, Error>> GetCollectionSearchLookupAsync(Guid collectionId, CancellationToken cancellationToken);

    // Progress lookup contracts (service-to-service)
    Task<Result<CourseDto, Error>> GetCourseLookupAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every non-archived course id on the platform. Used by AccessService to
    /// expand global FULL_ALL / LEARN_ALL plan grants into per-course read models.
    /// </summary>
    Task<Result<IReadOnlyList<Guid>, Error>> GetAllCourseIdsAsync(
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns every non-archived course id of an author. Used by author-filtered
    /// read models and legacy FREE-grant expansion.
    /// </summary>
    Task<Result<IReadOnlyList<Guid>, Error>> GetAuthorCourseIdsAsync(
        Guid authorId,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>> GetCourseProgressBlueprintsAsync(
        GetCourseProgressBlueprintsRequest request,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyCollection<ResolvedMaterialDto>, Error>> ResolveMaterialTargetsAsync(
        ResolveMaterialTargetsRequest request,
        CancellationToken cancellationToken);

    Task<Result<ModuleDto, Error>> GetModuleLookupAsync(
        Guid courseId,
        Guid moduleId,
        CancellationToken cancellationToken);

    Task<Result<ProjectDto, Error>> GetProjectLookupAsync(
        Guid courseId,
        Guid projectId,
        CancellationToken cancellationToken);

    Task<Result<MaterialDto, Error>> GetMaterialLookupAsync(
        Guid moduleId,
        Guid materialId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Записывает сгенерированный конспект в <c>Material.Content</c>. Идемпотентно,
    ///     проверяет совпадение <c>VideoId</c>. Service-to-service эндпоинт для
    ///     MaterialProcessingService после завершения content-draft job'а.
    /// </summary>
    Task<UnitResult<Error>> UpdateMaterialContentAsync(
        Guid materialId,
        UpdateMaterialContentRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Денормализует главы видео (заголовок + offset в секундах) в
    ///     <c>Material.ChapterTitles</c> + <c>Material.ChapterTimestamps</c> у всех
    ///     материалов, привязанных к указанному видео — для индексации в поиске и
    ///     deep-link'а на конкретный таймкод. Вызывается MaterialProcessingService
    ///     после успешного PUT глав в Kinescope. Идемпотентно. ECS делает fan-out +
    ///     публикует <c>material.updated</c> для каждого затронутого материала.
    /// </summary>
    Task<UnitResult<Error>> UpdateVideoChaptersAsync(
        Guid videoId,
        UpdateVideoChaptersRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Возвращает все пары (CourseId, ModuleId), в которых присутствует материал через
    ///     module_items. Используется ProgressService для cascade'а на module_item_progress.
    /// </summary>
    Task<Result<IReadOnlyList<MaterialCourseContextDto>, Error>> GetMaterialCourseContextsAsync(
        Guid materialId,
        CancellationToken cancellationToken);

    Task<Result<IssueDto, Error>> GetIssueLookupAsync(
        Guid projectId,
        Guid issueId,
        CancellationToken cancellationToken);

    Task<Result<EntityOwnershipDto, Error>> GetEntityOwnershipAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Batch-lookup заголовков материалов по ID. Используется CommentService author-feed'ом
    ///     для отображения «под каким материалом» оставлен коммент. Возвращает только
    ///     найденные записи; отсутствующие в БД ID просто не попадают в ответ.
    /// </summary>
    Task<Result<IReadOnlyList<MaterialTitleDto>, Error>> GetMaterialTitlesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Batch-lookup rich-summary материалов по ID (title, preview, kind, status,
    ///     access_type, image/video, thumbnail, views). Используется AccessService для
    ///     обогащения карточек закреплённых материалов на home-дашборде (epic #397).
    ///     S2S-only: возвращает summary независимо от доступа (caller делает свой
    ///     entitlement-чек). Тело материала (markdown content) не отдаётся. Отсутствующие
    ///     в БД ID просто не попадают в ответ.
    /// </summary>
    Task<Result<IReadOnlyList<MaterialSummaryDto>, Error>> GetMaterialSummariesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Batch-lookup primary привязки материалов к опубликованным курсам. Используется
    ///     CommentService inbox'ом для роутинга: если материал привязан к курсу, ссылка
    ///     ведёт на course-scoped material view, иначе — на standalone KB. Материалы
    ///     без привязки в ответ не попадают.
    /// </summary>
    Task<Result<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>> GetMaterialCourseBindingsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Batch-lookup primary привязки задач к опубликованным курсам (через project
    ///     → course_items). Используется CommentService inbox'ом для роутинга карточки
    ///     обсуждения на course-scoped issue view. Задачи без привязки в ответ не
    ///     попадают.
    /// </summary>
    Task<Result<IReadOnlyList<IssueCourseBindingLookupDto>, Error>> GetIssueCourseBindingsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Batch-lookup заголовков курсов по ID. Используется ProgressService'ом для
    ///     enrichment'а review-feed'а (отображение названия курса вместо GUID).
    /// </summary>
    Task<Result<IReadOnlyList<CourseTitleDto>, Error>> GetCourseTitlesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Batch-lookup заголовков проектов по ID. Используется ProgressService'ом для
    ///     enrichment'а review-feed'а (отображение названия проекта вместо GUID).
    /// </summary>
    Task<Result<IReadOnlyList<ProjectTitleDto>, Error>> GetProjectTitlesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Batch-lookup заголовков задач по ID. Используется ProgressService'ом для
    ///     enrichment'а review-feed'а (отображение названия задачи вместо GUID).
    /// </summary>
    Task<Result<IReadOnlyList<IssueTitleDto>, Error>> GetIssueTitlesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>S2S-ключ ответов для ProgressService. Доступен только SERVICE/ADMIN.
    ///     AccessType определяет проверку доступа к квизу.</summary>
    Task<Result<QuizAnswerKeyDto, Error>> GetQuizAnswerKeyAsync(
        Guid quizId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Возвращает все пары (CourseId, ModuleId), в которых квиз размещён через
    ///     module_items(item_type='Quiz') — зеркало <see cref="GetMaterialCourseContextsAsync"/>.
    ///     Используется ProgressService для cascade'а passed-попытки квиза на
    ///     module_item_progress (ST-13, #493).
    /// </summary>
    Task<Result<IReadOnlyList<QuizModuleContextDto>, Error>> GetQuizModuleLookupAsync(
        Guid quizId,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Batch-lookup summary квизов по ID (title + purpose + представительный курс —
    ///     MIN(course_id) из course_quizzes, null для standalone). Используется
    ///     ProgressService'ом для enrichment'а страницы «Мои тесты» и админ-статистикой
    ///     по тестам (#556). Отсутствующие в БД ID (квиз hard-deleted) в ответ не попадают.
    /// </summary>
    Task<Result<IReadOnlyList<QuizSummaryLookupDto>, Error>> GetQuizSummariesAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Глобальный контент еженедельного дайджеста (#532): опубликованные с
    ///     <paramref name="sinceUtc"/> материалы (с primary course-slug) и курсы.
    ///     Только метаданные. Consumer — NotificationService.WeeklyDigestRunner.
    /// </summary>
    Task<Result<DigestContentDto, Error>> GetDigestContentAsync(
        DateTime sinceUtc,
        int maxItemsPerKind,
        CancellationToken cancellationToken);
}