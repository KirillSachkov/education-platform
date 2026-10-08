namespace Shared.Messaging.IntegrationEvents.Files.Events;

public sealed record FileAssetBound(
    Guid AssetId,
    string Kind,
    string UsageType,
    Guid TargetEntityId,
    string TargetEntityType,
    long BindingRevision = 0,
    bool RequiresAuthoritativeConfirmation = false);
