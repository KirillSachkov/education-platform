namespace FileService.Contracts.Assets;

public sealed record InitiateVideoUploadResponse(
    Guid AssetId,
    string Status,
    string UploadUrl,
    string ProviderVideoId);
