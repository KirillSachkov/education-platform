using System.Data.Common;
using ContentAccess;
using Core.Abstractions;
using Core.Database;
using Dapper;
using EducationContentService.Contracts.Courses;
using EducationContentService.Core.Features.AuthorCredit;
using EducationContentService.Domain;
using EducationContentService.Domain.Courses;
using EducationContentService.Domain.Modules;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using Framework.Endpoints;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using static Dapper.SqlMapper;

namespace EducationContentService.Core.Features.Courses.Queries;

public sealed record GetCurriculumQuery(Guid CourseId, string? AccessFilter) : IQuery;

public sealed class GetCurriculumEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("courses/{courseId:guid}/curriculum", async Task<EndpointResult<CourseCurriculumDto>> (
                    [FromRoute] Guid courseId,
                    [FromQuery] string? accessFilter,
                    [FromServices] GetCurriculumHandler handler,
                    CancellationToken cancellationToken) =>
                await handler.Handle(new GetCurriculumQuery(courseId, accessFilter), cancellationToken))
            .AllowAnonymousEndpoint()
            .RequireRateLimiting("anonymous-read");
    }
}

public sealed class GetCurriculumHandler : IQueryHandlerWithResult<CourseCurriculumDto, GetCurriculumQuery>
{
    private static readonly HybridCacheEntryOptions _cacheOptions = new()
    {
        Expiration = TimeSpan.FromMinutes(3),
        LocalCacheExpiration = TimeSpan.FromSeconds(30),
    };

    private readonly ITransactionManager _transactionManager;
    private readonly IFileServiceClient _fileServiceClient;
    private readonly IEntitlementChecker _entitlementChecker;
    private readonly IAuthorLookupClient _authorLookupClient;
    private readonly HybridCache _cache;
    private readonly ILogger<GetCurriculumHandler> _logger;
    private readonly UserScopedData _userScopedData;

    public GetCurriculumHandler(
        ITransactionManager transactionManager,
        IFileServiceClient fileServiceClient,
        IEntitlementChecker entitlementChecker,
        IAuthorLookupClient authorLookupClient,
        HybridCache cache,
        ILogger<GetCurriculumHandler> logger,
        UserScopedData userScopedData)
    {
        _transactionManager = transactionManager;
        _fileServiceClient = fileServiceClient;
        _entitlementChecker = entitlementChecker;
        _authorLookupClient = authorLookupClient;
        _cache = cache;
        _logger = logger;
        _userScopedData = userScopedData;
    }

    public async Task<Result<CourseCurriculumDto, Error>> Handle(
        GetCurriculumQuery query, CancellationToken cancellationToken)
    {
        // Determine the cache bucket once per request to avoid per-user cache explosion.
        // Variance is binary (private articles visible or not), so three shared buckets suffice:
        //   anon     — unauthenticated OR authenticated-without-entitlement (public articles only)
        //   manage   — users with Courses.MANAGE or Articles.MANAGE (bypass entitlement check)
        //   enrolled — authenticated users entitled to this course (without manage perm)
        bool canSeePrivateArticles = await CanSeePrivateCourseArticles(query.CourseId, cancellationToken);
        string cacheKey = GetCacheKey(query.CourseId, canSeePrivateArticles);
        Error? fetchError = null;

        CourseCurriculumDto? cached = await _cache.GetOrCreateAsync(
            cacheKey,
            async ct =>
            {
                Result<CourseCurriculumDto, Error> result = await FetchCurriculum(query, canSeePrivateArticles, ct);
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
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            return fetchError;
        }

        if (cached is null)
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            return GeneralErrors.NotFound(query.CourseId);
        }

        // Apply post-cache filter for accessFilter=free. We don't bake the filter into
        // the cache key — three buckets (anon/enrolled/manage) × N filter combinations
        // would explode keyspace for a free-tier feature. Free filter is cheap to apply
        // on read since the DTO already includes AccessType per item.
        if (IsFreeAccessFilter(query.AccessFilter))
        {
            cached = ApplyFreeOnlyFilter(cached);
        }

        return cached;
    }

    private static bool IsFreeAccessFilter(string? accessFilter) =>
        !string.IsNullOrWhiteSpace(accessFilter)
        && accessFilter.Equals("free", StringComparison.OrdinalIgnoreCase);

    private static CourseCurriculumDto ApplyFreeOnlyFilter(CourseCurriculumDto curriculum)
    {
        List<CurriculumSectionDto> filteredSections = curriculum.Sections
            .Select(section => section with
            {
                Items = section.Items
                    .Where(item => IsFreeAccessType(item.AccessType))
                    .ToList(),
            })
            .Where(section => section.Items.Count > 0)
            .ToList();

        return curriculum with { Sections = filteredSections };
    }

    private static bool IsFreeAccessType(string? accessType) =>
        accessType is not null
        && (accessType.Equals("PUBLIC", StringComparison.Ordinal)
            || accessType.Equals("REGISTERED", StringComparison.Ordinal)
            || accessType.Equals("FREE", StringComparison.Ordinal));

    private async Task<Result<CourseCurriculumDto, Error>> FetchCurriculum(
        GetCurriculumQuery query, bool canSeePrivateArticles, CancellationToken cancellationToken)
    {
        DbConnection connection = _transactionManager.GetDbConnection();

        const string sql = """
                      SELECT
                          c.id, c.author_id, c.slug, c.title, c.description, c.status, c.kind,
                          c.learning_outcomes, c.target_audience, c.prerequisites,
                          c.image_id, c.getting_started_module_id,
                          c.is_new, c.created_at, c.updated_at
                      FROM courses c
                      WHERE c.id = @CourseId;

                      SELECT
                          ci.id, ci.reference_id, ci.item_type, ci.sort_key, ci.is_optional,
                          COALESCE(m.title, p.title) AS title,
                          COALESCE(m.description, p.description) AS description,
                          COALESCE(m.detailed_description, p.detailed_description) AS detailed_description,
                          COALESCE(m.status, p.status) AS status
                      FROM course_items ci
                      LEFT JOIN modules m ON ci.item_type = 'Module' AND ci.reference_id = m.id
                      LEFT JOIN projects p ON ci.item_type = 'Project' AND ci.reference_id = p.id
                      WHERE ci.course_id = @CourseId
                        AND COALESCE(m.status, p.status) = 'PUBLISHED'
                      ORDER BY ci.sort_key;

                      SELECT
                          mi.id, mi.module_id, mi.reference_id, mi.item_type, mi.sort_key, mi.is_optional,
                          mi.view_priority,
                          COALESCE(mat.title, i.title, q.title) AS title,
                          COALESCE(mat.status, i.status, q.status) AS status,
                          COALESCE(mat.access_type, i.access_type, q.access_type) AS access_type,
                          mat.video_id, mat.image_id, mat.kind,
                          CASE WHEN mi.item_type = 'Quiz' THEN jsonb_array_length(q.questions) END AS questions_count
                      FROM module_items mi
                      JOIN course_items ci ON ci.reference_id = mi.module_id AND ci.item_type = 'Module'
                      LEFT JOIN materials mat ON mi.item_type = 'Material' AND mat.id = mi.reference_id
                      LEFT JOIN issues i ON mi.item_type = 'Issue' AND i.id = mi.reference_id
                      LEFT JOIN quizzes q ON mi.item_type = 'Quiz' AND q.id = mi.reference_id
                      WHERE ci.course_id = @CourseId
                        AND COALESCE(mat.status, i.status, q.status) = 'PUBLISHED'
                      ORDER BY mi.module_id, mi.sort_key;

                      SELECT
                          pi.id, pi.project_id, pi.issue_id, pi.sort_key, pi.is_optional,
                          i.title, i.status, i.access_type
                      FROM project_items pi
                      JOIN course_items ci ON ci.reference_id = pi.project_id AND ci.item_type = 'Project'
                      JOIN issues i ON i.id = pi.issue_id
                      WHERE ci.course_id = @CourseId
                        AND i.status = 'PUBLISHED'
                      ORDER BY pi.project_id, pi.sort_key;

                      -- Опубликованные подборки курса (#508) — отдельный блок «Подборки» под
                      -- модулями на странице программы и в курс-сайдбаре. Считаем только
                      -- PUBLISHED-материалы (DISTINCT — материал может лежать в двух секциях),
                      -- зеркаля знаменатели прогресс-blueprint'а (#496).
                      SELECT
                          col.id,
                          col.title,
                          col.description,
                          col.access_type,
                          col.cover_image_id,
                          COUNT(DISTINCT m.id)::integer AS items_count,
                          ARRAY_REMOVE(ARRAY_AGG(DISTINCT m.id), NULL) AS material_ids
                      FROM collections col
                      LEFT JOIN collection_sections cs ON cs.collection_id = col.id
                      LEFT JOIN collection_items ci ON ci.section_id = cs.id AND ci.item_type = 'MATERIAL'
                      LEFT JOIN materials m ON m.id = ci.reference_id AND m.status = 'PUBLISHED'
                      WHERE col.course_id = @CourseId
                        AND col.status = 'PUBLISHED'
                      GROUP BY col.id
                      ORDER BY col.is_pinned DESC, col.pinned_sort_key ASC NULLS LAST,
                               col.updated_at DESC, col.id DESC;
                      """;

        var command = new CommandDefinition(sql, new { query.CourseId }, cancellationToken: cancellationToken);
        await using GridReader multi = await connection.QueryMultipleAsync(command);

        CourseRow? courseRow = await multi.ReadFirstOrDefaultAsync<CourseRow>();
        if (courseRow is null)
            return GeneralErrors.NotFound(query.CourseId);

        if (courseRow.Status != PublicationStatus.PUBLISHED)
            return GeneralErrors.NotFound(query.CourseId);

        var sectionRows = (await multi.ReadAsync<SectionRow>()).ToList();
        var moduleItemRows = (await multi.ReadAsync<ModuleItemRow>()).ToList();
        var projectItemRows = (await multi.ReadAsync<ProjectItemRow>()).ToList();
        var collectionRows = (await multi.ReadAsync<CollectionRow>()).ToList();

        // После унификации Lesson+Article → Material is_public-фильтрация больше не применяется —
        // доступ управляется AccessType на уровне Material.

        // Batch-fetch images (covers) + videos (thumb + duration) for all materials.
        // Manual cover (ImageId) wins over Kinescope video thumbnail — same priority as
        // MaterialFeedEnricher; explicit author intent beats auto-generated thumb.
        List<Guid> videoIds = moduleItemRows
            .Where(mi => mi.VideoId is not null)
            .Select(mi => mi.VideoId!.Value)
            .Distinct()
            .ToList();

        // Material covers + collection covers идут одним FileService-батчем.
        List<Guid> imageIds = moduleItemRows
            .Where(mi => mi.ImageId is not null)
            .Select(mi => mi.ImageId!.Value)
            .Concat(collectionRows
                .Where(c => c.CoverImageId is not null)
                .Select(c => c.CoverImageId!.Value))
            .Distinct()
            .ToList();

        Task<Dictionary<Guid, GetPublicVideoResponse>> videoTask = videoIds.Count > 0
            ? LoadVideosAsync(videoIds, query.CourseId, cancellationToken)
            : Task.FromResult(new Dictionary<Guid, GetPublicVideoResponse>());

        Task<Dictionary<Guid, string>> imageTask = imageIds.Count > 0
            ? LoadImageUrlsAsync(imageIds, query.CourseId, cancellationToken)
            : Task.FromResult(new Dictionary<Guid, string>());

        await Task.WhenAll(videoTask, imageTask);
        Dictionary<Guid, GetPublicVideoResponse> videoMap = await videoTask;
        Dictionary<Guid, string> imageUrlMap = await imageTask;

        var moduleItemsByModule = moduleItemRows.GroupBy(r => r.ModuleId).ToDictionary(g => g.Key, g => g.ToList());
        var projectItemsByProject = projectItemRows.GroupBy(r => r.ProjectId).ToDictionary(g => g.Key, g => g.ToList());

        List<CurriculumSectionDto> sections = [];
        int globalModulePosition = 1;
        foreach (SectionRow section in sectionRows)
        {
            if (section.Title is null)
            {
                _logger.LogWarning(
                    "Course {CourseId} has course_item {ItemId} ({ItemType}) pointing to a non-existent entity {ReferenceId}",
                    query.CourseId, section.Id, section.ItemType, section.ReferenceId);
                continue;
            }

            List<CurriculumItemDto>? items = section.ItemType switch
            {
                CourseItemType.Module => moduleItemsByModule
                    .GetValueOrDefault(section.ReferenceId, [])
                    .Select(mi => new CurriculumItemDto(
                        mi.ReferenceId, mi.ItemType.ToString(), mi.Title, mi.SortKey, mi.IsOptional,
                        globalModulePosition++, mi.AccessType, mi.ViewPriority,
                        mi.Kind,
                        ResolveDuration(mi, videoMap),
                        ResolveCoverUrl(mi, imageUrlMap, videoMap),
                        mi.QuestionsCount,
                        mi.ItemType == ModuleItemType.Quiz ? mi.ReferenceId : null))
                    .ToList(),

                CourseItemType.Project => projectItemsByProject
                    .GetValueOrDefault(section.ReferenceId, [])
                    .Select((pi, idx) => new CurriculumItemDto(
                        pi.IssueId, nameof(ModuleItemType.Issue), pi.Title, pi.SortKey, pi.IsOptional,
                        idx + 1, pi.AccessType))
                    .ToList(),

                _ => null
            };

            if (items is null)
            {
                _logger.LogWarning(
                    "Course {CourseId} has course_item {ItemId} with unknown CourseItemType {ItemType}; skipping",
                    query.CourseId, section.Id, section.ItemType);
                continue;
            }

            sections.Add(new CurriculumSectionDto(
                section.ReferenceId, section.ItemType.ToString(), section.Title, section.Description,
                section.DetailedDescription, section.SortKey, section.IsOptional, items));
        }

        string? imageUrl = null;
        if (courseRow.ImageId is not null)
        {
            Result<GetFileResponse?, Error> fileResult =
                await _fileServiceClient.GetFileAsync(courseRow.ImageId.Value, cancellationToken);

            if (fileResult is { IsSuccess: true, Value: not null })
                imageUrl = fileResult.Value.ContentUrl;
            else
                _logger.LogWarning(
                    "Failed to fetch image {ImageId} for course {CourseId}",
                    courseRow.ImageId,
                    query.CourseId);
        }

        // Issue #358: AccessType.FREE удалён; «бесплатный контент» теперь = PUBLIC или REGISTERED.
        bool hasFreeContent = sections.Any(s =>
            s.Items.Any(i =>
                string.Equals(i.AccessType, "PUBLIC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(i.AccessType, "REGISTERED", StringComparison.OrdinalIgnoreCase)));

        List<CurriculumCollectionDto> collections = collectionRows
            .Select(c => new CurriculumCollectionDto(
                c.Id, c.Title, c.Description, c.AccessType,
                c.ItemsCount, c.MaterialIds ?? [],
                c.CoverImageId is not null && imageUrlMap.TryGetValue(c.CoverImageId.Value, out string? coverUrl)
                    ? coverUrl
                    : null))
            .ToList();

        (string? authorName, string? authorAvatarUrl) =
            await ResolveAuthorCreditAsync(courseRow.AuthorId, cancellationToken);

        return new CourseCurriculumDto(
            courseRow.Id, courseRow.AuthorId, courseRow.Slug, courseRow.Title,
            courseRow.Description, courseRow.Status.ToString(), courseRow.Kind,
            courseRow.ImageId, imageUrl, courseRow.GettingStartedModuleId,
            hasFreeContent, courseRow.IsNew,
            courseRow.CreatedAt, courseRow.UpdatedAt,
            sections,
            courseRow.LearningOutcomes,
            courseRow.TargetAudience,
            courseRow.Prerequisites,
            collections,
            AuthorDisplayName: authorName,
            AuthorAvatarUrl: authorAvatarUrl);
    }

    // Resolves the author's display name + avatar URL for the public course-overview byline
    // (#569). Both best-effort — degraded AuthService / FileService leaves the credit null and
    // never fails the page. Runs inside the cached factory → one lookup per cache bucket / 3-min TTL.
    private async Task<(string? DisplayName, string? AvatarUrl)> ResolveAuthorCreditAsync(
        Guid authorId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyDictionary<Guid, AuthorCreditDto>, Error> authorsResult =
            await _authorLookupClient.GetAuthorsByIdsAsync([authorId], cancellationToken);

        if (authorsResult.IsFailure
            || !authorsResult.Value.TryGetValue(authorId, out AuthorCreditDto? author))
        {
            return (null, null);
        }

        string? avatarUrl = null;
        if (author.AvatarId is { } avatarId)
        {
            Result<GetFileResponse?, Error> fileResult =
                await _fileServiceClient.GetFileAsync(avatarId, cancellationToken);
            if (fileResult is { IsSuccess: true, Value: not null })
                avatarUrl = fileResult.Value.ContentUrl;
        }

        return (author.DisplayName, avatarUrl);
    }

    private sealed class CourseRow
    {
        public Guid Id { get; init; }
        public Guid AuthorId { get; init; }
        public string Slug { get; init; } = null!;
        public string Title { get; init; } = null!;
        public string Description { get; init; } = null!;
        public string Kind { get; init; } = null!;
        public string[] LearningOutcomes { get; init; } = [];
        public string[] TargetAudience { get; init; } = [];
        public string[] Prerequisites { get; init; } = [];
        public PublicationStatus Status { get; init; }
        public Guid? ImageId { get; init; }
        public Guid? GettingStartedModuleId { get; init; }
        public bool IsNew { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    private sealed class SectionRow
    {
        public Guid Id { get; init; }
        public Guid ReferenceId { get; init; }
        public CourseItemType ItemType { get; init; }
        public string SortKey { get; init; } = null!;
        public bool IsOptional { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public string? DetailedDescription { get; init; }
        public PublicationStatus? Status { get; init; }
    }

    private sealed class ModuleItemRow
    {
        public Guid Id { get; init; }
        public Guid ModuleId { get; init; }
        public Guid ReferenceId { get; init; }
        public ModuleItemType ItemType { get; init; }
        public string SortKey { get; init; } = null!;
        public bool IsOptional { get; init; }
        public string ViewPriority { get; init; } = null!;
        public string Title { get; init; } = null!;
        public PublicationStatus Status { get; init; }
        public string? AccessType { get; init; }
        public Guid? VideoId { get; init; }
        public Guid? ImageId { get; init; }
        public string? Kind { get; init; }
        public int? QuestionsCount { get; init; }
    }

    private sealed class ProjectItemRow
    {
        public Guid Id { get; init; }
        public Guid ProjectId { get; init; }
        public Guid IssueId { get; init; }
        public string SortKey { get; init; } = null!;
        public bool IsOptional { get; init; }
        public string Title { get; init; } = null!;
        public PublicationStatus Status { get; init; }
        public string? AccessType { get; init; }
    }

    private sealed class CollectionRow
    {
        public Guid Id { get; init; }
        public string Title { get; init; } = null!;
        public string? Description { get; init; }
        public string AccessType { get; init; } = null!;
        public Guid? CoverImageId { get; init; }
        public int ItemsCount { get; init; }
        public Guid[]? MaterialIds { get; init; }
    }

    private async Task<bool> CanSeePrivateCourseArticles(Guid courseId, CancellationToken cancellationToken)
    {
        if (_userScopedData.IsAuthenticated && (
                _userScopedData.HasPermission(PlatformPermissions.Courses.MANAGE) ||
                _userScopedData.HasPermission(PlatformPermissions.Articles.MANAGE)))
            return true;

        if (!_userScopedData.IsAuthenticated)
            return false;

        AccessDecision decision = await _entitlementChecker.CheckAccessAsync(
            _userScopedData.ToAccessSubject(),
            ResourceTypes.COURSE,
            courseId,
            cancellationToken);

        return decision.IsGranted;
    }

    private string GetCacheKey(Guid courseId, bool canSeePrivateArticles)
    {
        if (_userScopedData.IsAuthenticated && (
                _userScopedData.HasPermission(PlatformPermissions.Courses.MANAGE) ||
                _userScopedData.HasPermission(PlatformPermissions.Articles.MANAGE)))
            return $"curriculum:{courseId}:manage";

        return canSeePrivateArticles
            ? $"curriculum:{courseId}:enrolled"
            : $"curriculum:{courseId}:anon";
    }

    private async Task<Dictionary<Guid, GetPublicVideoResponse>> LoadVideosAsync(
        IReadOnlyList<Guid> videoIds, Guid courseId, CancellationToken ct)
    {
        Dictionary<Guid, GetPublicVideoResponse> map = [];
        const int batchSize = 50;
        foreach (Guid[] chunk in videoIds.Chunk(batchSize))
        {
            var result = await _fileServiceClient.GetVideosBatchAsync(chunk, ct);
            if (result is { IsSuccess: true, Value: not null })
            {
                foreach (GetPublicVideoResponse video in result.Value)
                    map[video.Id] = video;
            }
            else
            {
                _logger.LogWarning(
                    "Failed to fetch videos for course {CourseId} (chunk of {Count})",
                    courseId, chunk.Length);
            }
        }
        return map;
    }

    private async Task<Dictionary<Guid, string>> LoadImageUrlsAsync(
        IReadOnlyList<Guid> imageIds, Guid courseId, CancellationToken ct)
    {
        Dictionary<Guid, string> map = [];
        const int batchSize = 50;
        foreach (Guid[] chunk in imageIds.Chunk(batchSize))
        {
            var result = await _fileServiceClient.GetFilesBatchAsync(chunk, ct);
            if (result is { IsSuccess: true, Value: not null })
            {
                foreach (GetFileResponse file in result.Value)
                {
                    if (file.ContentUrl is not null)
                        map[file.Id] = file.ContentUrl;
                }
            }
            else
            {
                _logger.LogWarning(
                    "Failed to fetch image covers for course {CourseId} (chunk of {Count})",
                    courseId, chunk.Length);
            }
        }
        return map;
    }

    private static double? ResolveDuration(
        ModuleItemRow row,
        IReadOnlyDictionary<Guid, GetPublicVideoResponse> videoMap)
    {
        if (row.VideoId is null) return null;
        return videoMap.TryGetValue(row.VideoId.Value, out GetPublicVideoResponse? video)
            ? video.DurationSeconds
            : null;
    }

    private static string? ResolveCoverUrl(
        ModuleItemRow row,
        IReadOnlyDictionary<Guid, string> imageUrlMap,
        IReadOnlyDictionary<Guid, GetPublicVideoResponse> videoMap)
    {
        // Manual cover wins; video thumb is fallback for VIDEO materials without one.
        if (row.ImageId is not null
            && imageUrlMap.TryGetValue(row.ImageId.Value, out string? imageUrl))
            return imageUrl;

        if (row.VideoId is not null
            && videoMap.TryGetValue(row.VideoId.Value, out GetPublicVideoResponse? video)
            && video.ThumbnailUrl is not null)
            return video.ThumbnailUrl;

        return null;
    }
}
