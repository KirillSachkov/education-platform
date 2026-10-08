using EducationContentService.Core.Features.Materials;
using EducationContentService.Domain;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace EducationContentService.Core.Features;

public static class OwnershipExtensions
{
    /// <summary>
    ///     Returns success if the caller is the resource author, a content moderator, or a
    ///     platform admin. A null <paramref name="authorId"/> means the resource is orphan
    ///     (no course / not yet attached) — only admins / moderators can mutate orphan content.
    ///     Moderators (<see cref="PlatformPermissions.Content.MODERATE"/>) manage any author's
    ///     content, bypassing this Tier-2 ownership check exactly like admins.
    /// </summary>
    public static UnitResult<Error> CheckOwnership(this UserScopedData user, Guid? authorId)
    {
        if (user.IsAdmin || user.HasPermission(PlatformPermissions.Content.MODERATE))
            return UnitResult.Success<Error>();

        if (authorId.HasValue && authorId.Value == user.UserId)
            return UnitResult.Success<Error>();

        return EducationErrors.AuthorshipRequired();
    }

    /// <summary>
    ///     Material-scoped ownership (#657). Success if the caller passes the base
    ///     <see cref="CheckOwnership"/> (admin / content moderator / material author) OR
    ///     owns a course that contains the material. This lets a course owner manage
    ///     materials placed in their course even when another author is the material's
    ///     author — e.g. an admin who added a draft material to someone else's course.
    ///     The course lookup only runs when the cheap base check fails.
    /// </summary>
    public static async Task<UnitResult<Error>> CheckMaterialOwnershipAsync(
        this UserScopedData user,
        Guid materialId,
        Guid? materialAuthorId,
        IMaterialsRepository materials,
        CancellationToken cancellationToken = default)
    {
        if (user.CheckOwnership(materialAuthorId).IsSuccess)
            return UnitResult.Success<Error>();

        if (await materials.IsMaterialInOwnedCourseAsync(materialId, user.UserId, cancellationToken))
            return UnitResult.Success<Error>();

        return EducationErrors.AuthorshipRequired();
    }
}
