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
using Microsoft.Extensions.Caching.Hybrid;

namespace EducationContentService.Contracts.HttpCommunication;

/// <summary>
/// Decorator-обёртка над <see cref="IEducationContentServiceClient"/> с кешированием
/// lookup-методов через <see cref="HybridCache"/> (Redis + L1 in-process).
///
/// Кешируются только те методы, которые: (а) часто дёргаются consumer'ами enrichment'а
/// (NotificationService рендерит десятки тысяч уведомлений с одинаковыми course/issue title)
/// и (б) данные относительно стабильны — title редактируется редко, инвалидация по
/// <c>course.updated</c> / <c>issue.updated</c> events закрывает stale window.
///
/// Detail / Export / Progress-blueprint методы НЕ кешируются — они либо тяжёлые,
/// либо требуют свежести (write-path).
/// </summary>
public sealed class CachedEducationContentServiceClient : IEducationContentServiceClient
{
    public const string COURSE_LOOKUP_KEY_PREFIX = "ecs:course-lookup:";
    public const string COURSE_SEARCH_LOOKUP_KEY_PREFIX = "ecs:course-search:";
    public const string ISSUE_SEARCH_LOOKUP_KEY_PREFIX = "ecs:issue-search:";
    public const string MATERIAL_SEARCH_LOOKUP_KEY_PREFIX = "ecs:material-search:";
    public const string ALL_COURSE_IDS_KEY = "ecs:all-course-ids";
    public const string AUTHOR_COURSE_IDS_KEY_PREFIX = "ecs:author-course-ids:";

    private readonly IEducationContentServiceClient _inner;
    private readonly HybridCache _cache;

    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(15),
        LocalCacheExpiration = TimeSpan.FromMinutes(2),
    };

    public CachedEducationContentServiceClient(IEducationContentServiceClient inner, HybridCache cache)
    {
        _inner = inner;
        _cache = cache;
    }

    // ===== CACHED LOOKUPS =====

    public Task<Result<CourseDto, Error>> GetCourseLookupAsync(
        Guid courseId, CancellationToken cancellationToken)
        => GetOrFetchAsync($"{COURSE_LOOKUP_KEY_PREFIX}{courseId}",
            ct => _inner.GetCourseLookupAsync(courseId, ct),
            cancellationToken);

    public Task<Result<CourseSearchLookupDto, Error>> GetCourseSearchLookupAsync(
        Guid courseId, CancellationToken cancellationToken)
        => GetOrFetchAsync($"{COURSE_SEARCH_LOOKUP_KEY_PREFIX}{courseId}",
            ct => _inner.GetCourseSearchLookupAsync(courseId, ct),
            cancellationToken);

    public Task<Result<IssueSearchLookupDto, Error>> GetIssueSearchLookupAsync(
        Guid issueId, CancellationToken cancellationToken)
        => GetOrFetchAsync($"{ISSUE_SEARCH_LOOKUP_KEY_PREFIX}{issueId}",
            ct => _inner.GetIssueSearchLookupAsync(issueId, ct),
            cancellationToken);

    public Task<Result<MaterialSearchLookupDto, Error>> GetMaterialSearchLookupAsync(
        Guid materialId, CancellationToken cancellationToken)
        => GetOrFetchAsync($"{MATERIAL_SEARCH_LOOKUP_KEY_PREFIX}{materialId}",
            ct => _inner.GetMaterialSearchLookupAsync(materialId, ct),
            cancellationToken);

    /// <summary>
    /// Кешируется: глобальный список курсов нужен для FULL_ALL / LEARN_ALL covered-courses.
    /// Список меняется редко; TTL ограничивает stale-window для новых курсов.
    /// </summary>
    public Task<Result<IReadOnlyList<Guid>, Error>> GetAllCourseIdsAsync(
        CancellationToken cancellationToken)
        => GetOrFetchAsync(ALL_COURSE_IDS_KEY,
            ct => _inner.GetAllCourseIdsAsync(ct),
            cancellationToken);

    /// <summary>
    /// Кешируется: список курсов автора меняется только при create/hard-delete курса,
    /// читается на hot-path author-filter/free-grant derive-моделей. TTL-based
    /// invalidation приемлем — задержка появления нового курса ограничена
    /// <see cref="_cacheOptions"/> Expiration.
    /// Пустой список (автор без курсов) — валидный non-null результат, кешируется.
    /// </summary>
    public Task<Result<IReadOnlyList<Guid>, Error>> GetAuthorCourseIdsAsync(
        Guid authorId, CancellationToken cancellationToken)
        => GetOrFetchAsync($"{AUTHOR_COURSE_IDS_KEY_PREFIX}{authorId}",
            ct => _inner.GetAuthorCourseIdsAsync(authorId, ct),
            cancellationToken);

    /// <summary>
    /// Generic кеш-обёртка с graceful degradation: на failure — возвращаем error и
    /// удаляем закешированное значение (не отравляем кеш negative result'ом).
    ///
    /// Edge case: если fetcher возвращает Success с null-value (теоретически возможно,
    /// если ECS отдал 200 с пустым body) — очищаем кеш, чтобы не закешировать null на
    /// 15 минут и не отдавать ложный NotFound.
    /// </summary>
    private async Task<Result<T, Error>> GetOrFetchAsync<T>(
        string key,
        Func<CancellationToken, Task<Result<T, Error>>> fetcher,
        CancellationToken cancellationToken)
        where T : class
    {
        Error? fetchError = null;

        T? cached = await _cache.GetOrCreateAsync<T?>(
            key,
            async ct =>
            {
                Result<T, Error> result = await fetcher(ct);
                if (result.IsFailure)
                {
                    fetchError = result.Error;
                    return null;
                }
                return result.Value;
            },
            _cacheOptions,
            cancellationToken: cancellationToken);

        if (fetchError is not null)
        {
            await _cache.RemoveAsync(key, cancellationToken);
            return fetchError;
        }

        if (cached is null)
        {
            // Success + null value — не отравляем кеш. Следующий вызов пойдёт в HTTP снова.
            await _cache.RemoveAsync(key, cancellationToken);
            return Error.NotFound("ecs.lookup.not_found", "Запись не найдена");
        }

        return cached;
    }

    // ===== NOT CACHED — pass-through =====

    public Task<Result<CourseDetailDto, Error>> GetCourseDetailAsync(
        Guid courseId, CancellationToken cancellationToken)
        => _inner.GetCourseDetailAsync(courseId, cancellationToken);

    public Task<Result<ModuleDetailDto, Error>> GetModuleDetailAsync(
        Guid moduleId, CancellationToken cancellationToken)
        => _inner.GetModuleDetailAsync(moduleId, cancellationToken);

    public Task<Result<ProjectDetailDto, Error>> GetProjectDetailAsync(
        Guid projectId, CancellationToken cancellationToken)
        => _inner.GetProjectDetailAsync(projectId, cancellationToken);

    public Task<Result<IssueDetailDto, Error>> GetDetailIssueByIdAsync(
        Guid issueId, CancellationToken cancellationToken)
        => _inner.GetDetailIssueByIdAsync(issueId, cancellationToken);

    public Task<Result<ModuleSearchLookupDto, Error>> GetModuleSearchLookupAsync(
        Guid moduleId, CancellationToken cancellationToken)
        => _inner.GetModuleSearchLookupAsync(moduleId, cancellationToken);

    public Task<Result<ProjectSearchLookupDto, Error>> GetProjectSearchLookupAsync(
        Guid projectId, CancellationToken cancellationToken)
        => _inner.GetProjectSearchLookupAsync(projectId, cancellationToken);

    public Task<Result<IReadOnlyCollection<CourseProgressBlueprintDto>, Error>> GetCourseProgressBlueprintsAsync(
        GetCourseProgressBlueprintsRequest request, CancellationToken cancellationToken)
        => _inner.GetCourseProgressBlueprintsAsync(request, cancellationToken);

    public Task<Result<IReadOnlyCollection<ResolvedMaterialDto>, Error>> ResolveMaterialTargetsAsync(
        ResolveMaterialTargetsRequest request, CancellationToken cancellationToken)
        => _inner.ResolveMaterialTargetsAsync(request, cancellationToken);

    public Task<Result<ModuleDto, Error>> GetModuleLookupAsync(
        Guid courseId, Guid moduleId, CancellationToken cancellationToken)
        => _inner.GetModuleLookupAsync(courseId, moduleId, cancellationToken);

    public Task<Result<ProjectDto, Error>> GetProjectLookupAsync(
        Guid courseId, Guid projectId, CancellationToken cancellationToken)
        => _inner.GetProjectLookupAsync(courseId, projectId, cancellationToken);

    public Task<Result<MaterialDto, Error>> GetMaterialLookupAsync(
        Guid moduleId, Guid materialId, CancellationToken cancellationToken)
        => _inner.GetMaterialLookupAsync(moduleId, materialId, cancellationToken);

    public Task<UnitResult<Error>> UpdateMaterialContentAsync(
        Guid materialId,
        UpdateMaterialContentRequest request,
        CancellationToken cancellationToken)
        => _inner.UpdateMaterialContentAsync(materialId, request, cancellationToken);

    public Task<UnitResult<Error>> UpdateVideoChaptersAsync(
        Guid videoId,
        UpdateVideoChaptersRequest request,
        CancellationToken cancellationToken)
        => _inner.UpdateVideoChaptersAsync(videoId, request, cancellationToken);

    public Task<Result<IssueDto, Error>> GetIssueLookupAsync(
        Guid projectId, Guid issueId, CancellationToken cancellationToken)
        => _inner.GetIssueLookupAsync(projectId, issueId, cancellationToken);

    public Task<Result<CollectionSearchLookupDto, Error>> GetCollectionSearchLookupAsync(
        Guid collectionId, CancellationToken cancellationToken)
        => _inner.GetCollectionSearchLookupAsync(collectionId, cancellationToken);

    public Task<Result<IReadOnlyList<MaterialCourseContextDto>, Error>> GetMaterialCourseContextsAsync(
        Guid materialId, CancellationToken cancellationToken)
        => _inner.GetMaterialCourseContextsAsync(materialId, cancellationToken);

    public Task<Result<EntityOwnershipDto, Error>> GetEntityOwnershipAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken)
        => _inner.GetEntityOwnershipAsync(entityType, entityId, cancellationToken);

    public Task<Result<IReadOnlyList<MaterialTitleDto>, Error>> GetMaterialTitlesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => _inner.GetMaterialTitlesAsync(ids, cancellationToken);

    // Не кешируется: батч-lookup по произвольному набору id (каждый set = новый ключ),
    // плюс summary несёт ViewsCount, который часто меняется — pass-through, как у titles.
    public Task<Result<IReadOnlyList<MaterialSummaryDto>, Error>> GetMaterialSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => _inner.GetMaterialSummariesAsync(ids, cancellationToken);

    public Task<Result<IReadOnlyList<MaterialCourseBindingLookupDto>, Error>> GetMaterialCourseBindingsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => _inner.GetMaterialCourseBindingsAsync(ids, cancellationToken);

    public Task<Result<IReadOnlyList<IssueCourseBindingLookupDto>, Error>> GetIssueCourseBindingsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => _inner.GetIssueCourseBindingsAsync(ids, cancellationToken);

    public Task<Result<IReadOnlyList<CourseTitleDto>, Error>> GetCourseTitlesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => _inner.GetCourseTitlesAsync(ids, cancellationToken);

    public Task<Result<IReadOnlyList<ProjectTitleDto>, Error>> GetProjectTitlesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => _inner.GetProjectTitlesAsync(ids, cancellationToken);

    public Task<Result<IReadOnlyList<IssueTitleDto>, Error>> GetIssueTitlesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => _inner.GetIssueTitlesAsync(ids, cancellationToken);

    // НЕ кешируется: ключ ответов участвует в грейдинге — stale-ответы после
    // правки квиза автором недопустимы.
    public Task<Result<QuizAnswerKeyDto, Error>> GetQuizAnswerKeyAsync(
        Guid quizId, CancellationToken cancellationToken)
        => _inner.GetQuizAnswerKeyAsync(quizId, cancellationToken);

    // НЕ кешируется: участвует в каскаде прогресса (passed-попытка → module_item_progress) —
    // pass-through, как GetMaterialCourseContextsAsync (ST-13 #493).
    public Task<Result<IReadOnlyList<QuizModuleContextDto>, Error>> GetQuizModuleLookupAsync(
        Guid quizId, CancellationToken cancellationToken)
        => _inner.GetQuizModuleLookupAsync(quizId, cancellationToken);

    // Не кешируется: батч-lookup по произвольному набору id (каждый set = новый ключ) —
    // pass-through, как GetCourseTitlesAsync / GetMaterialSummariesAsync (#556).
    public Task<Result<IReadOnlyList<QuizSummaryLookupDto>, Error>> GetQuizSummariesAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
        => _inner.GetQuizSummariesAsync(ids, cancellationToken);

    // НЕ кешируется: дайджест дёргается раз в неделю одним фоновым проходом (#532).
    public Task<Result<DigestContentDto, Error>> GetDigestContentAsync(
        DateTime sinceUtc, int maxItemsPerKind, CancellationToken cancellationToken)
        => _inner.GetDigestContentAsync(sinceUtc, maxItemsPerKind, cancellationToken);
}
