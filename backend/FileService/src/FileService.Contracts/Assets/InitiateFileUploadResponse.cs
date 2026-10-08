namespace FileService.Contracts.Assets;

public sealed record InitiateFileUploadResponse(
    Guid AssetId,
    string Status,
    string UploadUrl,
    IReadOnlyDictionary<string, string> RequiredHeaders,
    string ContentUrl);
