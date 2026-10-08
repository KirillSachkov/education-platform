using CSharpFunctionalExtensions;
using PlatformAuth.Middleware;
using SharedKernel;

namespace MaterialProcessingService.Core.Features;

/// <summary>
///     Локальный аналог <c>EducationContentService.Core.Features.OwnershipExtensions.CheckOwnership</c>.
///     Каноничный shared-помощник пока не вынесен в Shared/Authentication из-за сервис-специфичных
///     error-codes (`content.authorship.required` ECS vs нужный нам `video.authorship.required`).
///     Когда этот pattern закрепится в третьем сервисе — поднять в Shared/Authentication
///     с параметризованным errorCode.
/// </summary>
public static class OwnershipExtensions
{
    public static UnitResult<Error> CheckOwnership(this UserScopedData user, Guid? ownerUserId)
    {
        if (user.IsAdmin)
            return UnitResult.Success<Error>();

        if (ownerUserId.HasValue && ownerUserId.Value == user.UserId)
            return UnitResult.Success<Error>();

        return Error.Authorization(
            "video.authorship.required",
            "Операция доступна только владельцу видео");
    }
}
