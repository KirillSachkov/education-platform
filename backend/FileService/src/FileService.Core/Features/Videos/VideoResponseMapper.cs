using FileService.Contracts.Assets;
using FileService.Contracts.Dtos;
using FileService.Domain;

namespace FileService.Core.Features.Videos;

internal static class VideoResponseMapper
{
    public static GetVideoResponse ToVideoResponse(
        this MediaAsset asset,
        VideoProviderRef? providerRef = null,
        bool includeExternalVideoId = true) =>
        new(
            asset.Id,
            asset.Kind.ToString().ToLowerInvariant(),
            asset.UsageType.ToApiString(),
            asset.Status.ToApiString(),
            asset.FileName.Value,
            asset.ContentType.Value,
            asset.Size,
            asset.TargetEntity is null ? null : new TargetEntityDto(asset.TargetEntity.Type, asset.TargetEntity.Id),
            asset.IsTemporary,
            includeExternalVideoId ? providerRef?.ExternalAssetId : null,
            providerRef?.Metadata?.ThumbnailUrl,
            providerRef?.Metadata?.DurationSeconds);
}
