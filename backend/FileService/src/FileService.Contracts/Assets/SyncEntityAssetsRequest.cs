using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

public sealed record SyncEntityAssetsRequest(
    TargetEntityDto TargetEntity,
    IReadOnlyList<string> UsageTypes,
    IReadOnlyList<Guid> ActiveAssetIds);
