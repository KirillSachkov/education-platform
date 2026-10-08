using AuthService.Core.Database;
using AuthService.Domain;
using Core.Database;
using Shared.Messaging.IntegrationEvents.Auth.Events;
using Shared.Messaging.IntegrationEvents.Files;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace AuthService.Core.Features.Users.EventHandlers;

public sealed class AvatarAssetDeletedHandler
{
    private readonly ITransactionManager _transactionManager;
    private readonly IProfileRepository _profiles;
    private readonly IOutboxService _outboxService;
    private readonly ILogger<AvatarAssetDeletedHandler> _logger;

    public AvatarAssetDeletedHandler(
        ITransactionManager transactionManager,
        IProfileRepository profiles,
        IOutboxService outboxService,
        ILogger<AvatarAssetDeletedHandler> logger)
    {
        _transactionManager = transactionManager;
        _profiles = profiles;
        _outboxService = outboxService;
        _logger = logger;
    }

    public async Task HandleAsync(FileAssetDeleted message, CancellationToken cancellationToken)
    {
        if (!string.Equals(message.UsageType, FileEventsRouting.UsageTypes.AVATAR, StringComparison.Ordinal) ||
            !string.Equals(message.TargetEntityType, FileEventsRouting.EntityTypes.USER, StringComparison.Ordinal))
        {
            return;
        }

        if (message.TargetEntityId is null)
        {
            return;
        }

        UserProfile? profile = await _profiles.GetByAsync(
            item => item.Id == message.TargetEntityId.Value,
            cancellationToken);
        if (profile?.AvatarId != message.AssetId ||
            profile.AvatarBindingRevision != message.BindingRevision)
            return;

        profile.UpdateAvatar(null, DateTime.UtcNow, message.BindingRevision);
        await _outboxService.PublishAsync(
            new UserAvatarUpdated(message.TargetEntityId.Value, null));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            _logger.LogError(
                "Failed to save avatar deletion: {Error}",
                saveResult.Error.GetMessage());
            throw saveResult.Error.AsTransient().ToException();
        }

        _logger.LogInformation(
            "Avatar deleted: AssetId={AssetId}, UserId={UserId}, RowsAffected={Rows}",
            message.AssetId, message.TargetEntityId, 1);
    }
}
