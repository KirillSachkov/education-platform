using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace MaterialProcessingService.Core.Cleanup;

/// <summary>
///     Reaction to <see cref="FileAssetDeleted"/> на exchange'е <c>file.events</c>.
///     Чистит транскрипт, timecode-job'ы и content-job'ы для удалённого видео.
///     Фильтр: только VIDEO usage type — preview-картинки и markdown-файлы pipeline
///     не касается.
/// </summary>
public static class FileAssetDeletedVideoHandler
{
    public static async Task HandleAsync(
        FileAssetDeleted message,
        IMaterialProcessingCleanup cleanup,
        ILogger<FileAssetDeleted> logger,
        CancellationToken cancellationToken)
    {
        if (!IsVideoUsage(message.UsageType))
        {
            return;
        }

        int deleted = await cleanup.DeleteByVideoAssetIdAsync(
            message.AssetId,
            cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Removed {Count} processing rows for deleted video asset {AssetId} (usage={UsageType})",
                deleted, message.AssetId, message.UsageType);
        }
    }

    private static bool IsVideoUsage(string? usageType) =>
        string.Equals(usageType, FileEventsRouting.UsageTypes.MATERIAL_VIDEO, StringComparison.Ordinal) ||
        string.Equals(usageType, FileEventsRouting.UsageTypes.COURSE_VIDEO, StringComparison.Ordinal);
}
