namespace FileService.Infrastructure.Kinescope.Responses;

internal sealed record KinescopeVideoAsset(
    string Id,
    string? OriginalName,
    long? FileSize,
    string? Filetype,
    string? Quality,
    string? Resolution,
    string? Url,
    string? DownloadLink);
