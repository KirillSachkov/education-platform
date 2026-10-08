namespace FileService.Infrastructure.Kinescope.Responses;

internal sealed record KinescopeInitResponse(KinescopeInitData? Data);

internal sealed record KinescopeInitData(string Id, string Endpoint);