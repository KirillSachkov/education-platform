using Microsoft.Extensions.Logging;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace MaterialProcessingService.Core.Cleanup;

/// <summary>
///     Reaction to <see cref="MaterialHardDeleted"/> event на exchange'е <c>education.events</c>.
///     Удаляет content-генерации, привязанные к материалу. Транскрипт и timecode-job'ы
///     остаются — они живут на video-asset'е, не на materialId, и могут пригодиться
///     если позже автор пересоздаст материал на том же video.
/// </summary>
public static class MaterialHardDeletedHandler
{
    public static async Task HandleAsync(
        MaterialHardDeleted message,
        IMaterialProcessingCleanup cleanup,
        ILogger<MaterialHardDeleted> logger,
        CancellationToken cancellationToken)
    {
        int deleted = await cleanup.DeleteContentJobsByMaterialIdAsync(
            message.MaterialId,
            cancellationToken);

        if (deleted > 0)
        {
            logger.LogInformation(
                "Removed {Count} content_generation_jobs for hard-deleted material {MaterialId}",
                deleted, message.MaterialId);
        }
    }
}
