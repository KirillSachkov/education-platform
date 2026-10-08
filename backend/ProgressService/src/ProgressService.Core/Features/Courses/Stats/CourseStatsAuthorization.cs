using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.ProgressLookup;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using ProgressService.Domain;

namespace ProgressService.Core.Features.Courses.Stats;

/// <summary>
///     Общий ownership-гейт для author per-course статистики (#634). Зеркало
///     <c>GetCourseStudents</c> lines 110–123: курс резолвится из ECS (он владеет
///     <c>Course.AuthorId</c>), доступ — admin/moderator (привилегированные) либо
///     автор-владелец курса. Tier-2 ownership поверх Tier-1 <c>Courses.MANAGE</c>.
/// </summary>
internal static class CourseStatsAuthorization
{
    public static async Task<Result<CourseDto, Error>> AuthorizeCourseManagementAsync(
        IEducationContentServiceClient educationContentServiceClient,
        UserScopedData user,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        Result<CourseDto, Error> courseResult =
            await educationContentServiceClient.GetCourseLookupAsync(courseId, cancellationToken);
        if (courseResult.IsFailure)
        {
            return courseResult.Error;
        }

        bool isPrivileged = user.HasRole(PlatformRoles.ADMIN) || user.HasRole(PlatformRoles.MODERATOR);
        bool isAuthorOwner = user.HasRole(PlatformRoles.AUTHOR) && courseResult.Value.AuthorId == user.UserId;
        if (!isPrivileged && !isAuthorOwner)
        {
            return ProgressErrors.CourseManagementForbidden(courseId);
        }

        return courseResult.Value;
    }
}
