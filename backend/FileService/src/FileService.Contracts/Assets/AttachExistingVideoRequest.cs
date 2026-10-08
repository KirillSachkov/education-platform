using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

public sealed record AttachExistingVideoRequest(
    string ExternalVideoId,
    string UsageType,
    TargetEntityDto? TargetEntity = null,
    Guid? DraftId = null);
