using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

public sealed record InitiateFileUploadRequest(
    string FileName,
    string ContentType,
    long Size,
    string UsageType,
    Guid? DraftId,
    TargetEntityDto? TargetEntity);
