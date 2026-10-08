using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Domain;

namespace FileService.Core.Features.Files;

internal static class FileResponseMapper
{
    public static GetFileResponse ToFileResponse(this MediaAsset asset, string contentUrl) =>
        new(
            asset.Id,
            asset.Kind.ToString().ToLowerInvariant(),
            asset.UsageType.ToApiString(),
            asset.Status.ToApiString(),
            asset.FileName.Value,
            asset.ContentType.Value,
            asset.Size,
            contentUrl,
            asset.TargetEntity is null ? null : new TargetEntityDto(asset.TargetEntity.Type, asset.TargetEntity.Id),
            asset.IsTemporary);
}
