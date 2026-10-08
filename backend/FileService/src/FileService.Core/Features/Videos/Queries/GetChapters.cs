using Core.Abstractions;
using FileService.Contracts.Assets;
using FileService.Core.Repositories;
using FileService.Core.Services;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace FileService.Core.Features.Videos.Queries;

public sealed record GetVideoChaptersQuery(Guid VideoId) : IQuery;

public sealed class GetVideoChaptersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/videos/{videoId:guid}/chapters/", async Task<EndpointResult<GetVideoChaptersResponse>> (
                [FromRoute] Guid videoId,
                [FromServices] IQueryHandlerWithResult<GetVideoChaptersResponse, GetVideoChaptersQuery> handler,
                CancellationToken token) =>
                await handler.Handle(new GetVideoChaptersQuery(videoId), token))
            .RequirePermissions(PlatformPermissions.Videos.READ);
    }
}

public sealed class GetVideoChaptersHandler : IQueryHandlerWithResult<GetVideoChaptersResponse, GetVideoChaptersQuery>
{
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IVideoProviderRefRepository _providerRefRepository;
    private readonly IVideoProvider _videoProvider;
    private readonly UserScopedData _user;

    public GetVideoChaptersHandler(
        IMediaAssetRepository assetRepository,
        IVideoProviderRefRepository providerRefRepository,
        IVideoProvider videoProvider,
        UserScopedData user)
    {
        _assetRepository = assetRepository;
        _providerRefRepository = providerRefRepository;
        _videoProvider = videoProvider;
        _user = user;
    }

    public async Task<Result<GetVideoChaptersResponse, Error>> Handle(
        GetVideoChaptersQuery query,
        CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult =
            await _assetRepository.GetByAsync(x => x.Id == query.VideoId, cancellationToken);
        if (assetResult.IsFailure)
            return assetResult.Error;

        MediaAsset asset = assetResult.Value;
        if (asset.Kind != AssetKind.VIDEO)
            return Error.Validation("asset.kind.invalid", "Главы доступны только для видео");

        bool isPrivileged = _user.IsAdmin || _user.HasRole(PlatformRoles.SERVICE);
        if (!isPrivileged && asset.UploadedByUserId != _user.UserId)
            return Error.Authorization("video.not.owner", "Нет доступа к данному видео");

        if (asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
            return GeneralErrors.NotFound(query.VideoId);

        Result<VideoProviderRef, Error> providerRefResult =
            await _providerRefRepository.GetByAsync(x => x.AssetId == query.VideoId, cancellationToken);

        if (providerRefResult.IsFailure)
        {
            if (providerRefResult.Error.Type == ErrorType.NOT_FOUND)
                return new GetVideoChaptersResponse(query.VideoId, []);

            return providerRefResult.Error;
        }

        Result<IReadOnlyList<VideoProviderChapter>, Error> chaptersResult =
            await _videoProvider.GetChaptersAsync(providerRefResult.Value.ExternalAssetId, cancellationToken);

        if (chaptersResult.IsFailure)
            return chaptersResult.Error;

        return new GetVideoChaptersResponse(
            query.VideoId,
            chaptersResult.Value
                .OrderBy(x => x.StartSeconds)
                .Select((chapter, index) => new VideoChapterDto(
                    chapter.Id,
                    chapter.Title,
                    chapter.StartSeconds,
                    index))
                .ToArray());
    }
}
