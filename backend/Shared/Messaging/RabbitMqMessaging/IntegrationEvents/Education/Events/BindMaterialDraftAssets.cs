namespace Shared.Messaging.IntegrationEvents.Education.Events;

public sealed record BindMaterialDraftAssets(
    Guid MaterialId,
    string DraftId,
    Guid? ActorUserId = null,
    bool ActorCanManageAnyAsset = false);
