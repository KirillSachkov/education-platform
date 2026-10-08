namespace Shared.Messaging.IntegrationEvents.Files.Events;

/// <summary>
///     Confirms that the authoritative aggregate committed a binding revision.
///     FileService uses this acknowledgement before treating asynchronous video
///     readiness as eligible for downstream processing.
/// </summary>
public sealed record FileAssetBindingConfirmed(Guid AssetId, long BindingRevision);
