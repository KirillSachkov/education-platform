namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record CourseAssetOwnershipChanged(
    Guid CourseId,
    long OwnershipRevision,
    Guid NewOwnerId,
    IReadOnlyList<AssetOwnershipTarget> Targets);

public sealed record AssetOwnershipTarget(string Type, Guid Id);
