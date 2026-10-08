using ContentAccess;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace PlatformAuth;

public static class AccessSubjectExtensions
{
    public static AccessSubject ToAccessSubject(this UserScopedData userData) => new(
        userData.IsAuthenticated,
        userData.IsAuthenticated ? userData.UserId : Guid.Empty,
        userData.IsAuthenticated && userData.HasPermission(PlatformPermissions.Platform.ADMIN));
}
