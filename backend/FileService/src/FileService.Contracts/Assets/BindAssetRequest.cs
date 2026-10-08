using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

public sealed record BindAssetRequest(
    TargetEntityDto TargetEntity,
    Guid? SelectionId = null);
