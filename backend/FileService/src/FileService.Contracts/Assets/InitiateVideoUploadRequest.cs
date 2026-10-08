using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

public sealed record InitiateVideoUploadRequest(
    string FileName,
    string ContentType,
    long Size,
    string UsageType,
    TargetEntityDto? TargetEntity = null,
    Guid? DraftId = null);
