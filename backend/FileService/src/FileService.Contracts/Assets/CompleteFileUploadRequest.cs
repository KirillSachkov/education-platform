namespace FileService.Contracts.Assets;

public sealed record CompleteFileUploadRequest(
    string? Checksum);
