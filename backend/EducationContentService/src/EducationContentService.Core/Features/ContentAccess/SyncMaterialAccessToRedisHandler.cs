using ContentAccess;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Rebuilds Redis access tags when a material's access type changes.
///     После #77 пробрасывает <c>AuthorId</c> в builder для гейтинга orphan FREE/ENROLLED.
/// </summary>
public static class SyncMaterialAccessToRedisHandler
{
    public static async Task HandleAsync(
        MaterialAccessChanged message,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger)
    {
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            message.AccessType,
            message.MaterialId,
            message.CourseIds,
            logger);

        await resourceAccessWriter.SetTagsAsync(ResourceTypes.MATERIAL, message.MaterialId, tags);

        logger.LogInformation(
            "Synced material access tags to Redis. MaterialId={MaterialId}, AccessType={AccessType}, AuthorId={AuthorId}, Tags=[{Tags}]",
            message.MaterialId, message.AccessType, message.AuthorId, string.Join(", ", tags));
    }
}
