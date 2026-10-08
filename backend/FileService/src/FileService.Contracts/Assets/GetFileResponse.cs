using FileService.Contracts.Dtos;

namespace FileService.Contracts.Assets;

public sealed record GetFileResponse(
    Guid Id,
    string Kind,
    string UsageType,
    string Status,
    string FileName,
    string ContentType,
    long Size,
    string? ContentUrl,
    TargetEntityDto? TargetEntity,
    bool IsTemporary);
