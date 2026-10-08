namespace Shared.Messaging.IntegrationEvents.Files.Events;

public sealed record VideoUploadInitiated(
    Guid AssetId,
    string Kind,
    string UsageType,
    Guid TargetEntityId,
    string TargetEntityType);
