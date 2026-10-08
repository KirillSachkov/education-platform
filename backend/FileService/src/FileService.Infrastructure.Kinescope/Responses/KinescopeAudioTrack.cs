namespace FileService.Infrastructure.Kinescope.Responses;

internal sealed record KinescopeAudioTrack(
    string Id,
    string? Language,
    string? Label,
    long? FileSize,
    string? Filetype,
    string? OriginalName,
    string? DownloadLink);
