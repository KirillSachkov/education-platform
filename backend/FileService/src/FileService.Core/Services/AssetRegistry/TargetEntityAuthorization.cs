using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Ownership;
using FileService.Domain;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace FileService.Core.Services.AssetRegistry;

public sealed class TargetEntityAuthorization : ITargetEntityAuthorization
{
    private readonly IEducationContentServiceClient _educationClient;
    private readonly UserScopedData _user;

    public TargetEntityAuthorization(
        IEducationContentServiceClient educationClient,
        UserScopedData user)
    {
        _educationClient = educationClient;
        _user = user;
    }

    public async Task<UnitResult<Error>> AuthorizeAsync(
        TargetEntity targetEntity,
        CancellationToken cancellationToken)
    {
        if (_user.IsAdmin)
            return UnitResult.Success<Error>();

        if (targetEntity.Type is "user" or "profile")
        {
            return targetEntity.Id == _user.UserId
                ? UnitResult.Success<Error>()
                : TargetNotOwned();
        }

        if (_user.HasPermission(PlatformPermissions.Content.MODERATE))
            return UnitResult.Success<Error>();

        return await AuthorizeManagerAsync(targetEntity, cancellationToken);
    }

    public async Task<UnitResult<Error>> AuthorizeManagerAsync(
        TargetEntity targetEntity,
        CancellationToken cancellationToken)
    {
        if (_user.IsAdmin)
            return UnitResult.Success<Error>();

        if (targetEntity.Type is "user" or "profile")
        {
            return targetEntity.Id == _user.UserId
                ? UnitResult.Success<Error>()
                : TargetNotOwned();
        }

        Result<EntityOwnershipDto, Error> ownershipResult =
            await _educationClient.GetEntityOwnershipAsync(
                targetEntity.Type,
                targetEntity.Id,
                cancellationToken);
        if (ownershipResult.IsFailure)
            return ownershipResult.Error;

        EntityOwnershipDto ownership = ownershipResult.Value;
        bool isManager = ownership.ManagerUserIds is not null
            ? ownership.ManagerUserIds.Contains(_user.UserId)
            : ownership.AuthorId == _user.UserId || ownership.CreatedByUserId == _user.UserId;
        return isManager
            ? UnitResult.Success<Error>()
            : TargetNotOwned();
    }

    private static Error TargetNotOwned() =>
        Error.Authorization("target.not.owner", "Нет доступа к целевой сущности");
}
