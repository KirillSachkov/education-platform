namespace FileService.Infrastructure.Kinescope.Responses;

internal sealed record KinescopeVideosListResponse(
    KinescopeVideoData[]? Data);
