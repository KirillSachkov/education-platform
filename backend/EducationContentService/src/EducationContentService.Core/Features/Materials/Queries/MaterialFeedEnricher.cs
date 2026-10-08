using ContentAccess;
using EducationContentService.Contracts.Materials;
using FileService.Contracts.Assets;
using FileService.Contracts.HttpCommunication;
using PlatformAuth;
using PlatformAuth.Middleware;
using ProgressService.Contracts.HttpCommunication;

namespace EducationContentService.Core.Features.Materials.Queries;

/// <summary>
/// Shared post-SQL enrichment for material feed endpoints:
///  - batch FileService calls for thumbnails,
///  - batch entitlement check,
///  - derivation of LockReason for inaccessible items.
/// </summary>
internal static class MaterialFeedEnricher
{
    public static async Task<List<MaterialFeedItemDto>> EnrichAsync(
        List<MaterialFeedItemDto> materials,
        IFileServiceClient fileServiceClient,
        IEntitlementChecker entitlementChecker,
        IProgressServiceClient progressServiceClient,
        UserScopedData userData,
        CancellationToken ct)
    {
        if (materials.Count == 0)
            return materials;

        // 1. Thumbnails (parallel image + video batches).
        List<Guid> imageIds = materials
            .Where(m => m.ImageId is not null)
            .Select(m => m.ImageId!.Value)
            .Distinct()
            .ToList();

        List<Guid> videoIds = materials
            .Where(m => m.VideoId is not null)
            .Select(m => m.VideoId!.Value)
            .Distinct()
            .ToList();

        Task<Dictionary<Guid, string>> imageTask = imageIds.Count > 0
            ? LoadImageUrlsAsync(fileServiceClient, imageIds, ct)
            : Task.FromResult(new Dictionary<Guid, string>());

        Task<Dictionary<Guid, GetPublicVideoResponse>> videoTask = videoIds.Count > 0
            ? LoadVideosAsync(fileServiceClient, videoIds, ct)
            : Task.FromResult(new Dictionary<Guid, GetPublicVideoResponse>());

        // 2. Batch entitlement check.
        List<Guid> materialIds = materials.Select(m => m.Id).ToList();
        Task<IReadOnlyDictionary<Guid, AccessDecision>> accessTask =
            entitlementChecker.CheckAccessBatchAsync(
                userData.ToAccessSubject(),
                ResourceTypes.MATERIAL,
                materialIds,
                ct);

        // 3. Enrolled course ids (only needed to distinguish trial_required vs standard_required).
        Task<IReadOnlySet<Guid>> trialCoursesTask = userData.IsAuthenticated
            ? entitlementChecker.GetUserEnrolledCourseIdsAsync(userData.UserId, includeTrial: true, ct)
            : Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        // 4. Views counts (auth + anon) — мягкий fail, 0 если упало.
        Task<IReadOnlyDictionary<Guid, long>> viewsTask =
            LoadMaterialViewsCountsAsync(progressServiceClient, materialIds, ct);

        Dictionary<Guid, string> imageUrlMap = await imageTask;
        Dictionary<Guid, GetPublicVideoResponse> videoMap = await videoTask;
        IReadOnlyDictionary<Guid, AccessDecision> accessMap = await accessTask;
        IReadOnlySet<Guid> trialOrStandardCourses = await trialCoursesTask;
        IReadOnlyDictionary<Guid, long> viewsMap = await viewsTask;

        return materials.Select(m =>
        {
            // Manual cover (ImageId) wins over auto Kinescope video thumbnail —
            // явная обложка автора это намерение, video-thumb лишь fallback
            // для VIDEO-материалов без кастомной обложки.
            GetPublicVideoResponse? video = m.VideoId.HasValue
                ? videoMap.GetValueOrDefault(m.VideoId.Value)
                : null;
            string? thumb = null;
            if (m.ImageId.HasValue)
                imageUrlMap.TryGetValue(m.ImageId.Value, out thumb);
            thumb ??= video?.ThumbnailUrl;

            bool isAccessible = true;
            string? lockReason = null;
            if (accessMap.TryGetValue(m.Id, out AccessDecision? decision) && !decision.IsGranted)
            {
                isAccessible = false;
                lockReason = DeriveLockReason(m, userData, trialOrStandardCourses);
            }

            long viewsCount = viewsMap.TryGetValue(m.Id, out long c) ? c : 0L;

            return m with
            {
                Preview = isAccessible ? m.Preview : null,
                ThumbnailUrl = thumb,
                IsAccessible = isAccessible,
                LockReason = lockReason,
                ViewsCount = viewsCount,
                DurationSeconds = video?.DurationSeconds,
            };
        }).ToList();
    }

    private static async Task<IReadOnlyDictionary<Guid, long>> LoadMaterialViewsCountsAsync(
        IProgressServiceClient progressServiceClient,
        IReadOnlyCollection<Guid> materialIds,
        CancellationToken ct)
    {
        Result<IReadOnlyDictionary<Guid, long>, Error> result =
            await progressServiceClient.GetMaterialViewsCountsAsync(materialIds, ct);

        // Soft-fail: счётчик не блокирует рендер списка.
        return result.IsSuccess
            ? result.Value
            : new Dictionary<Guid, long>();
    }

    private static string DeriveLockReason(
        MaterialFeedItemDto m,
        UserScopedData userData,
        IReadOnlySet<Guid> trialOrStandardCourses)
    {
        if (!userData.IsAuthenticated)
            return MaterialLockReasons.Anonymous;

        // FREE удалён в #358 (AccessType → PUBLIC|REGISTERED|ENROLLED) — ветка была мёртвой.

        if (string.Equals(m.AccessType, "ENROLLED", StringComparison.Ordinal))
        {
            if (m.CourseId.HasValue && trialOrStandardCourses.Contains(m.CourseId.Value))
                return MaterialLockReasons.StandardRequired;
            return MaterialLockReasons.NotEnrolled;
        }

        // Fallback for REGISTERED or other — denial for an authenticated user is unexpected.
        return MaterialLockReasons.NotEnrolled;
    }

    private static async Task<Dictionary<Guid, string>> LoadImageUrlsAsync(
        IFileServiceClient fileServiceClient, IReadOnlyList<Guid> imageIds, CancellationToken ct)
    {
        Dictionary<Guid, string> map = [];
        var result = await fileServiceClient.GetFilesBatchAsync(imageIds, ct);
        if (result is { IsSuccess: true, Value: not null })
        {
            foreach (GetFileResponse f in result.Value)
                if (f.ContentUrl is not null)
                    map[f.Id] = f.ContentUrl;
        }
        return map;
    }

    /// <summary>Публичные video-метаданные (thumbnail + duration) одним batch'ем — soft-fail в пустую map.</summary>
    internal static async Task<Dictionary<Guid, GetPublicVideoResponse>> LoadVideosAsync(
        IFileServiceClient fileServiceClient, IReadOnlyList<Guid> videoIds, CancellationToken ct)
    {
        Dictionary<Guid, GetPublicVideoResponse> map = [];
        var result = await fileServiceClient.GetVideosBatchAsync(videoIds, ct);
        if (result is { IsSuccess: true, Value: not null })
        {
            foreach (GetPublicVideoResponse v in result.Value)
                map[v.Id] = v;
        }
        return map;
    }

    /// <summary>
    /// Заполняет <see cref="MaterialSummaryDto.ThumbnailUrl"/> и <see cref="MaterialSummaryDto.ViewsCount"/>.
    /// Используется list-эндпоинтами без entitlement-обогащения (teaching/author KB).
    /// Изображение-обложка имеет приоритет; видео-кадр используется как fallback при отсутствии ImageId.
    /// </summary>
    public static async Task<List<MaterialSummaryDto>> EnrichSummaryThumbnailsAsync(
        List<MaterialSummaryDto> materials,
        IFileServiceClient fileServiceClient,
        IProgressServiceClient progressServiceClient,
        CancellationToken ct)
    {
        if (materials.Count == 0)
            return materials;

        List<Guid> imageIds = materials
            .Where(m => m.ImageId is not null)
            .Select(m => m.ImageId!.Value)
            .Distinct()
            .ToList();

        List<Guid> videoIds = materials
            .Where(m => m.VideoId is not null)
            .Select(m => m.VideoId!.Value)
            .Distinct()
            .ToList();

        Task<Dictionary<Guid, string>> imageTask = imageIds.Count > 0
            ? LoadImageUrlsAsync(fileServiceClient, imageIds, ct)
            : Task.FromResult(new Dictionary<Guid, string>());

        Task<Dictionary<Guid, GetPublicVideoResponse>> videoTask = videoIds.Count > 0
            ? LoadVideosAsync(fileServiceClient, videoIds, ct)
            : Task.FromResult(new Dictionary<Guid, GetPublicVideoResponse>());

        List<Guid> materialIds = materials.Select(m => m.Id).ToList();
        Task<IReadOnlyDictionary<Guid, long>> viewsTask =
            LoadMaterialViewsCountsAsync(progressServiceClient, materialIds, ct);

        Dictionary<Guid, string> imageUrlMap = await imageTask;
        Dictionary<Guid, GetPublicVideoResponse> videoMap = await videoTask;
        IReadOnlyDictionary<Guid, long> viewsMap = await viewsTask;

        return materials.Select(m =>
        {
            // Manual cover (ImageId) wins over auto Kinescope video thumbnail —
            // см. EnrichAsync: тот же приоритет.
            GetPublicVideoResponse? video = m.VideoId.HasValue
                ? videoMap.GetValueOrDefault(m.VideoId.Value)
                : null;
            string? thumb = null;
            if (m.ImageId.HasValue)
                imageUrlMap.TryGetValue(m.ImageId.Value, out thumb);
            thumb ??= video?.ThumbnailUrl;

            long viewsCount = viewsMap.TryGetValue(m.Id, out long c) ? c : 0L;

            return m with
            {
                ThumbnailUrl = thumb,
                ViewsCount = viewsCount,
                DurationSeconds = video?.DurationSeconds,
            };
        }).ToList();
    }
}
