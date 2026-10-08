namespace Shared.Messaging.IntegrationEvents.Files.Events;

/// <summary>
///     Confirms that an authoritative aggregate no longer references a particular
///     binding revision. FileService deletes the asset only while the revision still
///     matches, so a delayed event cannot delete an asset that was selected again.
/// </summary>
public sealed record FileAssetDetached(Guid AssetId, long ExpectedBindingRevision);
