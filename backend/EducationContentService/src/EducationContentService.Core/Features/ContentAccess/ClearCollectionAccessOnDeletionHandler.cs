using ContentAccess;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Удаляет Redis-теги при hard-delete подборки.
///     Симметрично <see cref="ClearMaterialAccessOnDeletionHandler"/>.
/// </summary>
public static class ClearCollectionAccessOnDeletionHandler
{
    public static async Task HandleAsync(
        CollectionHardDeleted message,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger)
    {
        await resourceAccessWriter.ClearTagsAsync(ResourceTypes.COLLECTION, message.CollectionId);

        logger.LogInformation(
            "Cleared access tags for deleted collection. CollectionId={CollectionId}",
            message.CollectionId);
    }
}
