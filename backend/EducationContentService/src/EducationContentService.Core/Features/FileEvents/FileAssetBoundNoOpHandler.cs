using Microsoft.Extensions.Caching.Hybrid;
using Shared.Messaging.IntegrationEvents.Files.Events;
using Wolverine;

namespace EducationContentService.Core.Features.FileEvents;

/// <summary>
///     ECS не записывает
///     <c>VideoId</c>/<c>PreviewId</c> по событию <see cref="FileAssetBound"/>
///     по событию: authoritative media revision сохраняется синхронно через
///     <c>IFileServiceClient.BindAssetAsync</c> в той же транзакции, что и aggregate.
///     <para>
///     FileService продолжает публиковать <see cref="FileAssetBound"/>:
///     (а) для <c>AvatarAssetBoundHandler</c> в AuthService,
///     (б) для re-publish'а из <c>VideoReconciliationService</c>, когда видео
///     переходит из <c>Processing</c> в <c>Ready</c> после Kinescope-обработки.
///     </para>
///     <para>
///     Issue #190: без invalidate'а HybridCache (5 min) <c>GetMaterialDetail</c>
///     продолжает отдавать stale status=processing на видео, который уже
///     стал ready в FileService. Этот handler сносит кэш asset'а — следующий
///     запрос детали материала пойдёт за свежим статусом и UI увидит ready.
///     </para>
/// </summary>
public sealed class FileAssetBoundCacheInvalidationHandler
{
    private readonly HybridCache _cache;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<FileAssetBoundCacheInvalidationHandler> _logger;

    public FileAssetBoundCacheInvalidationHandler(
        HybridCache cache,
        IMessageBus messageBus,
        ILogger<FileAssetBoundCacheInvalidationHandler> logger)
    {
        _cache = cache;
        _messageBus = messageBus;
        _logger = logger;
    }

    public async Task Handle(FileAssetBound message, CancellationToken cancellationToken)
    {
        if (string.Equals(message.Kind, "video", StringComparison.Ordinal))
        {
            await _cache.RemoveAsync(
                $"{CachedFileServiceClient.VIDEO_DETAIL_CACHE_KEY_PREFIX}{message.AssetId}",
                cancellationToken);
            await _cache.RemoveAsync(
                $"{CachedFileServiceClient.VIDEO_PUBLIC_CACHE_KEY_PREFIX}{message.AssetId}",
                cancellationToken);
        }
        else
        {
            await _cache.RemoveAsync(
                $"{CachedFileServiceClient.FILE_CACHE_KEY_PREFIX}{message.AssetId}",
                cancellationToken);
        }

        _logger.LogDebug(
            "Invalidated FileService cache for asset {AssetId} (kind={Kind}, usage={UsageType})",
            message.AssetId, message.Kind, message.UsageType);

        TimeSpan? verificationDelay = BindingVerificationPolicy.GetDelay(message);
        if (verificationDelay is { } delay)
        {
            await _messageBus.ScheduleAsync(
                new VerifyFileAssetBinding(
                    message.AssetId,
                    message.UsageType,
                    message.TargetEntityId,
                    message.TargetEntityType,
                    message.BindingRevision),
                delay);
        }
    }
}
