using EducationContentService.Core.Features;
using EducationContentService.Domain;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features.ProjectItems;

internal static class IssueInternalMaterialPolicy
{
    public static bool CanReference(
        Guid materialAuthorId,
        PublicationStatus status,
        AccessType accessType,
        UserScopedData user)
    {
        if (user.CheckOwnership(materialAuthorId).IsSuccess)
            return true;

        return status == PublicationStatus.PUBLISHED && accessType == AccessType.PUBLIC;
    }
}
