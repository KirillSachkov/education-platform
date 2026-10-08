using ContentAccess;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Пересчитывает Redis-теги при смене AccessType у подборки.
///     Симметрично <see cref="SyncMaterialAccessToRedisHandler"/>.
/// </summary>
public static class SyncCollectionAccessToRedisHandler
{
    public static async Task HandleAsync(
        CollectionAccessChanged message,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger)
    {
        Guid[] courseIds = message.CourseId is null ? [] : [message.CourseId.Value];
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            message.AccessType,
            message.CollectionId,
            courseIds,
            logger);

        await resourceAccessWriter.SetTagsAsync(ResourceTypes.COLLECTION, message.CollectionId, tags);

        logger.LogInformation(
            "Synced collection access tags to Redis. CollectionId={CollectionId}, AccessType={AccessType}, AuthorId={AuthorId}, Tags=[{Tags}]",
            message.CollectionId, message.AccessType, message.AuthorId, string.Join(", ", tags));
    }
}
