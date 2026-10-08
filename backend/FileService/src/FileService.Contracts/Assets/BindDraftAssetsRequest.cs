using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

public sealed record BindDraftAssetsRequest(
    Guid DraftId,
    TargetEntityDto TargetEntity,
    IReadOnlyList<Guid> AssetIds);
