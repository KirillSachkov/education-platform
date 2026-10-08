using ContentAccess;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Clears Redis access tags when a material is hard-deleted.
/// </summary>
public static class ClearMaterialAccessOnDeletionHandler
{
    public static async Task HandleAsync(
        MaterialHardDeleted message,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger)
    {
        await resourceAccessWriter.ClearTagsAsync(ResourceTypes.MATERIAL, message.MaterialId);

        logger.LogInformation(
            "Cleared access tags for deleted material. MaterialId={MaterialId}",
            message.MaterialId);
    }
}
