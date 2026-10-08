using ContentAccess;
using FileService.Core.Caching;
using FileService.Core.FilesStorage;
using FileService.Core.Repositories;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Options;
using PlatformAuth;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using SharedKernel.Exceptions;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace FileService.Core.Features.Files.Queries;

public sealed class GetFileContentEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/files/{fileId:guid}/content", HandleAsync)
            .AllowAnonymousEndpoint();
    }

    private static readonly HashSet<AssetUsageType> _protectedUsageTypes =
    [
        AssetUsageType.MARKDOWN_IMAGE,
        AssetUsageType.MARKDOWN_FILE,
    ];

    // These resource types have a first-class Redis entitlement projection.
    // Other markdown targets are fail-closed to uploader/admin until their own
    // entitlement projection exists (#741).
    private static readonly HashSet<string> _entitlementTargetTypes =
        new(StringComparer.OrdinalIgnoreCase) { "material", "issue" };

    // Cache-Control for the 302 itself so warm pages don't hit FileService on
    // every <img>. Protected files: short private cache so entitlement revocation
    // propagates quickly. Public files: longer public cache with `immutable` —
    // MediaAsset.Id is unique per upload, content under a given fileId never
    // changes.
    private const string PROTECTED_CACHE_CONTROL = "private, max-age=60";
    private const string PUBLIC_CACHE_CONTROL = "public, max-age=3600, immutable";

    // The max-age windows of the two Cache-Control headers above, as TimeSpans. The browser
    // keeps serving the cached 302 (and the presigned URL inside it) for this long, so
    // PresignedUrlCachePolicy reserves this window when sizing the server-side cache TTL —
    // otherwise the browser can replay an already-expired presigned URL → S3 403 (#438).
    // Keep these in sync with the max-age values in the strings above.
    private static readonly TimeSpan _protectedClientCacheMaxAge = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan _publicClientCacheMaxAge = TimeSpan.FromSeconds(3600);

    private static async Task<IResult> HandleAsync(
        Guid fileId,
        HttpContext httpContext,
        IMediaAssetRepository repository,
        IFileStorageRefRepository fileStorageRefRepository,
        IObjectStorageProvider objectStorageProvider,
        IEntitlementChecker entitlementChecker,
        UserScopedData userData,
        IOptions<FileStorageOptions> storageOptions,
        HybridCache cache,
        ILogger<GetFileContentEndpoint> logger,
        CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult =
            await repository.GetByAsync(a => a.Id == fileId, cancellationToken);

        if (assetResult.IsFailure || assetResult.Value.Kind != AssetKind.FILE || !assetResult.Value.IsContentReadable())
        {
            return Results.NotFound();
        }

        MediaAsset asset = assetResult.Value;

        // Every markdown asset is protected, including READY drafts. Bound material
        // and issue content uses the shared entitlement model. Project and
        // plan_onboarding_step currently have no resource-tag projection, so they
        // are uploader/admin-only instead of accidentally public.
        bool isProtected = _protectedUsageTypes.Contains(asset.UsageType);

        if (isProtected)
        {
            bool requiresEntitlement = !asset.IsTemporary
                && asset.TargetEntity is not null
                && _entitlementTargetTypes.Contains(asset.TargetEntity.Type);
            bool granted;
            if (requiresEntitlement)
            {
                AccessDecision decision = await entitlementChecker.CheckAccessAsync(
                    userData.ToAccessSubject(),
                    asset.TargetEntity!.Type,
                    asset.TargetEntity.Id,
                    cancellationToken);
                granted = decision.IsGranted;
            }
            else
            {
                granted = userData.IsAdmin ||
                          userData.IsAuthenticated && asset.UploadedByUserId == userData.UserId;
            }

            if (!granted)
            {
                logger.LogInformation(
                    "Access denied to file {FileId} (target {TargetType}:{TargetId}) for user {UserId}",
                    fileId, asset.TargetEntity?.Type, asset.TargetEntity?.Id, userData.UserId);

                return Results.Forbid();
            }
        }

        Result<FileStorageRef, Error> storageRefResult =
            await fileStorageRefRepository.GetByAsync(r => r.AssetId == asset.Id, cancellationToken);

        if (storageRefResult.IsFailure)
        {
            return Results.NotFound();
        }

        // #646: responsive variant selection. `?w=` picks the nearest generated WebP
        // variant whose width >= the (clamped) requested width. Falls back to the
        // original when: no/invalid `w`, asset has no variants, or every variant is
        // smaller than requested (i.e. w >= original width). No `?w=` ⇒ original,
        // byte-identical to the pre-#646 behaviour.
        int? requestedWidth = ParseRequestedWidth(httpContext.Request.Query["w"]);
        ImageVariant? variant = asset.SelectVariantForWidth(requestedWidth);

        string storageKey = variant?.StorageKey ?? storageRefResult.Value.StorageKey.Value;
        // Вложения скачиваем с original-именем; превью/аватары/markdown-картинки
        // отдаём inline, иначе Firefox/Safari блокируют их через
        // OpaqueResponseBlocking на cross-origin redirect к S3. Content-Type
        // прокидываем всегда — без него S3 на presigned-redirect возвращает
        // application/octet-stream, что тоже триггерит ORB. Variant → image/webp.
        string? downloadFileName = asset.UsageType == AssetUsageType.MARKDOWN_FILE && variant is null
            ? asset.FileName.Value
            : null;
        string contentType = variant?.ContentType ?? asset.ContentType.Value;

        // For protected content, key the cache per-user so a leaked URL can't be
        // replayed by a different user who also happens to pass the entitlement
        // check against the cached (global) value. For public content, the cache
        // is shared across users. #646: the chosen variant width is part of the key
        // so each width caches its own presigned URL (a 320px URL must never be
        // served for a 960px request).
        int? variantWidth = variant?.Width;
        string cacheKey = isProtected
            ? CacheKeys.FileDownloadUrl.ByIdAndUser(fileId, userData.UserId, variantWidth)
            : CacheKeys.FileDownloadUrl.ById(fileId, variantWidth);

        // PresignedUrlCachePolicy sizes the cache TTL so the presigned URL outlives the
        // browser's own cache of the 302, and caps it at a few hours regardless of the
        // configured presigned lifetime to limit the blast radius of URL sharing.
        int urlExpirationMinutes = isProtected
            ? storageOptions.Value.ProtectedDownloadUrlExpirationMinutes
            : storageOptions.Value.DownloadUrlExpirationMinutes;
        TimeSpan presignedLifetime = TimeSpan.FromMinutes(urlExpirationMinutes);
        TimeSpan clientCacheMaxAge = isProtected
            ? _protectedClientCacheMaxAge
            : _publicClientCacheMaxAge;
        TimeSpan cacheExpiration =
            PresignedUrlCachePolicy.ComputeServerCacheTtl(presignedLifetime, clientCacheMaxAge);

        string[] cacheTags = [CacheKeys.FileDownloadUrl.TagById(fileId)];

        string url;
        try
        {
            url = await cache.GetOrCreateAsync(
                cacheKey,
                async ct =>
                {
                    Result<string, Error> urlResult = await objectStorageProvider.GenerateDownloadUrlAsync(
                        storageKey,
                        presignedLifetime,
                        contentType,
                        downloadFileName,
                        ct);
                    if (urlResult.IsFailure)
                    {
                        // boundary: HybridCache delegate must throw to skip caching the failure —
                        // a single transient S3 blip would otherwise lock the file out for the
                        // full cacheExpiration window and turn a brief outage into hours of 503s.
                        throw new TransientException(urlResult.Error);
                    }

                    return urlResult.Value;
                },
                new HybridCacheEntryOptions
                {
                    Expiration = cacheExpiration,
                    LocalCacheExpiration = TimeSpan.FromMinutes(Math.Min(60, cacheExpiration.TotalMinutes / 2)),
                },
                tags: cacheTags,
                cancellationToken: cancellationToken);
        }
        catch (TransientException ex)
        {
            logger.LogWarning(
                ex,
                "Failed to generate presigned URL for file {FileId}: {ErrorType}",
                fileId,
                ex.Error.Type);
            return Results.StatusCode(503);
        }

        if (string.IsNullOrEmpty(url))
        {
            return Results.StatusCode(503);
        }

        httpContext.Response.Headers.CacheControl = isProtected
            ? PROTECTED_CACHE_CONTROL
            : PUBLIC_CACHE_CONTROL;

        return Results.Redirect(url, permanent: false, preserveMethod: false);
    }

    // Parses the `?w=` query value. Absent / non-numeric / non-positive ⇒ null
    // (→ original served). Clamping to buckets happens in ImageVariantPolicy.
    private static int? ParseRequestedWidth(Microsoft.Extensions.Primitives.StringValues raw)
    {
        string? first = raw.Count > 0 ? raw[0] : null;
        if (string.IsNullOrWhiteSpace(first))
        {
            return null;
        }

        return int.TryParse(first, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int w) && w > 0
            ? w
            : null;
    }
}
