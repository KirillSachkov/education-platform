using ContentAccess;
using EducationContentService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Записывает Redis-теги при создании подборки. Симметрично
///     <see cref="SyncMaterialAccessOnCreationHandler"/>. Для DRAFT-подборки теги
///     всё равно ставятся, чтобы GetCollectionDetail видел корректный lock-state
///     до первой публикации.
/// </summary>
public static class SyncCollectionAccessOnCreationHandler
{
    public static async Task HandleAsync(
        CollectionCreated message,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger)
    {
        if (!Enum.TryParse<AccessType>(message.AccessType, ignoreCase: true, out AccessType accessType))
        {
            logger.LogError(
                "Неизвестный AccessType '{AccessType}' в CollectionCreated для подборки {CollectionId}; сообщение будет повторено или отправлено в DLQ",
                message.AccessType, message.CollectionId);
            throw new InvalidOperationException(
                $"Unknown collection access type '{message.AccessType}' for {message.CollectionId}.");
        }

        Guid[] courseIds = message.CourseId is null ? [] : [message.CourseId.Value];
        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            accessType,
            message.CollectionId,
            courseIds,
            logger);

        await resourceAccessWriter.SetTagsAsync(ResourceTypes.COLLECTION, message.CollectionId, tags);

        logger.LogInformation(
            "Set initial access tags for new collection. CollectionId={CollectionId}, AccessType={AccessType}, AuthorId={AuthorId}, Tags=[{Tags}]",
            message.CollectionId, accessType, message.AuthorId, string.Join(", ", tags));
    }
}
