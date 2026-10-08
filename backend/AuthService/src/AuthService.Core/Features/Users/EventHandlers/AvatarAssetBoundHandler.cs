using AuthService.Core.Database;
using AuthService.Domain;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace AuthService.Core.Features.Users.EventHandlers;

public sealed class AvatarAssetBoundHandler
{
    private readonly ITransactionManager _transactionManager;
    private readonly IProfileRepository _profiles;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<AvatarAssetBoundHandler> _logger;

    public AvatarAssetBoundHandler(
        ITransactionManager transactionManager,
        IProfileRepository profiles,
        IOutboxService outboxService,
        ILogger<AvatarAssetBoundHandler> logger)
    {
        _transactionManager = transactionManager;
        _profiles = profiles;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task HandleAsync(FileAssetBound message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.UsageType, FileEventsRouting.UsageTypes.AVATAR, StringComparison.Ordinal) ||
            !string.Equals(message.TargetEntityType, FileEventsRouting.EntityTypes.USER, StringComparison.Ordinal))
        {
            return;
        }

        UserProfile? profile = await _profiles.GetByAsync(
            item => item.Id == message.TargetEntityId,
            cancellationToken);
        if (profile is null)
            return;

        if (message.BindingRevision <= profile.AvatarBindingRevision)
        {
            if (message.BindingRevision > 0 && profile.AvatarId != message.AssetId)
            {
                await _outboxService.PublishAsync(new FileAssetDetached(
                    message.AssetId,
                    message.BindingRevision));
                UnitResult<Error> staleSave = await _transactionManager.SaveChangesAsync(cancellationToken);
                if (staleSave.IsFailure)
                    throw staleSave.Error.AsTransient().ToException();
            }
            return;
        }

        Guid? previousAvatarId = profile.AvatarId;
        long previousBindingRevision = profile.AvatarBindingRevision;
        profile.UpdateAvatar(message.AssetId, DateTime.UtcNow, message.BindingRevision);

        await _outboxService.PublishAsync(
            new UserAvatarUpdated(message.TargetEntityId, message.AssetId));
        await _outboxService.PublishAsync(new FileAssetBindingConfirmed(
            message.AssetId,
            message.BindingRevision));

        if (previousAvatarId is { } previousId && previousId != message.AssetId)
        {
            await _outboxService.PublishAsync(new FileAssetDetached(
                previousId,
                previousBindingRevision));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to save avatar binding revision: {Error}",
                saveResult.Error.GetMessage());
            throw saveResult.Error.AsTransient().ToException();
        }

        _logger.LogInformation(
            "Avatar bound: AssetId={AssetId}, UserId={UserId}, RowsAffected={Rows}",
            message.AssetId, message.TargetEntityId, 1);
    }
}
