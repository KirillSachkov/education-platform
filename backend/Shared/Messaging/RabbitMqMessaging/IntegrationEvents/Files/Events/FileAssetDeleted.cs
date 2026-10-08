namespace Shared.Messaging.IntegrationEvents.Files.Events;

public sealed record FileAssetDeleted(
    Guid AssetId,
    string Kind,
    string UsageType,
    Guid? TargetEntityId,
    string? TargetEntityType,
    long BindingRevision = 0);
