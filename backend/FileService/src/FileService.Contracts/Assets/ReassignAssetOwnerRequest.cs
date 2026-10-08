using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

public sealed record ReassignAssetOwnerRequest(
    Guid NewOwnerId,
    IReadOnlyList<TargetEntityDto> TargetEntities);
