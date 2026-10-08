namespace FileService.Contracts.Assets;

public sealed record CompleteFileUploadResponse(
    Guid AssetId,
    string Status,
    string ContentUrl);
