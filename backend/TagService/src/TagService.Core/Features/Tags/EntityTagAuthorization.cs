using EducationContentService.Contracts.HttpCommunication;
using EducationContentService.Contracts.Ownership;
using PlatformAuth.Middleware;

namespace TagService.Core.Features.Tags;

public sealed class EntityTagAuthorization
{
    private readonly IEducationContentServiceClient _educationContentClient;
    private readonly UserScopedData _user;
    private readonly ILogger<EntityTagAuthorization> _logger;

    public EntityTagAuthorization(
        IEducationContentServiceClient educationContentClient,
        UserScopedData user,
        ILogger<EntityTagAuthorization> logger)
    {
        _educationContentClient = educationContentClient;
        _user = user;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> CheckCanManageAsync(
        string entityType,
        Guid entityId,
        CancellationToken cancellationToken)
    {
        if (_user.IsAdmin)
            return UnitResult.Success<Error>();

        Result<EntityOwnershipDto, Error> ownershipResult;
        try
        {
            ownershipResult = await _educationContentClient.GetEntityOwnershipAsync(
                entityType,
                entityId,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Ownership lookup failed for {EntityType}:{EntityId}; denying tag mutation for user {UserId}.",
                entityType,
                entityId,
                _user.UserId);
            return UnitResult.Failure<Error>(
                Error.Authorization("entity.ownership.unavailable", "Не удалось проверить владельца сущности"));
        }

        if (ownershipResult.IsFailure)
        {
            _logger.LogWarning(
                "Ownership lookup returned an error for {EntityType}:{EntityId}; denying tag mutation for user {UserId}.",
                entityType,
                entityId,
                _user.UserId);
            return UnitResult.Failure<Error>(
                Error.Authorization("entity.ownership.unavailable", "Не удалось проверить владельца сущности"));
        }

        EntityOwnershipDto ownership = ownershipResult.Value;
        bool canManage = ownership.ManagerUserIds?.Contains(_user.UserId) == true;

        if (canManage)
            return UnitResult.Success<Error>();

        _logger.LogWarning(
            "Authorization denied: user {UserId} attempted to mutate tags on {EntityType}:{EntityId} without ownership.",
            _user.UserId,
            entityType,
            entityId);
        return UnitResult.Failure<Error>(
            Error.Authorization("entity.not_owned", "Изменение тегов доступно только владельцу сущности"));
    }
}
