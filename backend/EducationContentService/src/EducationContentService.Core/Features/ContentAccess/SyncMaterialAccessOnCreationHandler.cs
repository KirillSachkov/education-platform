using ContentAccess;
using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain;
using Shared.Messaging.IntegrationEvents.Education.Events;

namespace EducationContentService.Core.Features.ContentAccess;

/// <summary>
///     Sets Redis access tags when a new material is created.
///     Uses the actual <see cref="AccessType"/> from the event, not a hardcoded default.
    ///     После #77 orphan ENROLLED гейтится через платформенный <c>plan:all</c>;
    ///     <c>AuthorId</c> остаётся в событии как ownership/audit context.
/// </summary>
public static class SyncMaterialAccessOnCreationHandler
{
    public static async Task HandleAsync(
        MaterialCreated message,
        IMaterialsRepository materialsRepository,
        IResourceAccessWriter resourceAccessWriter,
        ILogger logger)
    {
        List<Guid> courseIds = await materialsRepository.GetCourseIdsAsync(message.MaterialId);

        if (!Enum.TryParse<AccessType>(message.AccessType, ignoreCase: true, out AccessType accessType))
        {
            logger.LogWarning(
                "Неизвестный AccessType '{AccessType}' в событии MaterialCreated для материала {MaterialId}, используется ENROLLED",
                message.AccessType, message.MaterialId);
            accessType = AccessType.ENROLLED;
        }

        IReadOnlyList<string> tags = ContentAccessTagBuilder.Build(
            accessType,
            message.MaterialId,
            courseIds,
            logger);

        await resourceAccessWriter.SetTagsAsync(ResourceTypes.MATERIAL, message.MaterialId, tags);

        logger.LogInformation(
            "Set initial access tags for new material. MaterialId={MaterialId}, AccessType={AccessType}, AuthorId={AuthorId}, Tags=[{Tags}]",
            message.MaterialId, accessType, message.AuthorId, string.Join(", ", tags));
    }
}
