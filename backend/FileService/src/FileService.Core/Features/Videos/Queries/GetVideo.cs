using Core.Abstractions;
using FileService.Contracts.Assets;
using FileService.Core.Repositories;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace FileService.Core.Features.Videos.Queries;

public sealed record GetVideoQuery(Guid VideoId, bool Internal = false) : IQuery;

public sealed class GetVideoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/videos/{videoId:guid}", async Task<EndpointResult<GetVideoResponse?>> (
                [FromRoute] Guid videoId,
                [FromServices] IQueryHandlerWithResult<GetVideoResponse?, GetVideoQuery> handler,
                CancellationToken token) => await handler.Handle(new GetVideoQuery(videoId), token))
            .RequirePermissions(PlatformPermissions.Videos.READ);
    }
}

public sealed class GetVideoInternalEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/videos/{videoId:guid}/", async Task<EndpointResult<GetVideoResponse?>> (
                [FromRoute] Guid videoId,
                [FromServices] IQueryHandlerWithResult<GetVideoResponse?, GetVideoQuery> handler,
                CancellationToken token) => await handler.Handle(new GetVideoQuery(videoId, Internal: true), token))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetVideoHandler : IQueryHandlerWithResult<GetVideoResponse?, GetVideoQuery>
{
    private readonly IMediaAssetRepository _repository;
    private readonly IVideoProviderRefRepository _providerRefRepository;
    private readonly UserScopedData _user;

    public GetVideoHandler(
        IMediaAssetRepository repository,
        IVideoProviderRefRepository providerRefRepository,
        UserScopedData user)
    {
        _repository = repository;
        _providerRefRepository = providerRefRepository;
        _user = user;
    }

    public async Task<Result<GetVideoResponse?, Error>> Handle(GetVideoQuery query, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult =
            await _repository.GetByAsync(a => a.Id == query.VideoId, cancellationToken);
        if (assetResult.IsFailure)
        {
            if (assetResult.Error.Type == ErrorType.NOT_FOUND)
            {
                return Result.Success<GetVideoResponse?, Error>(null);
            }

            return assetResult.Error;
        }

        MediaAsset asset = assetResult.Value;
        if (asset.Kind != AssetKind.VIDEO)
        {
            return Result.Success<GetVideoResponse?, Error>(null);
        }

        if (query.Internal)
        {
            if (asset.IsTemporary || asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
                return Result.Success<GetVideoResponse?, Error>(null);
        }
        else
        {
            if (!_user.IsAdmin && asset.UploadedByUserId != _user.UserId)
                return Error.Authorization("video.not.owner", "Нет доступа к данному видео");

            if (asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
                return Result.Success<GetVideoResponse?, Error>(null);
        }

        VideoProviderRef? providerRef = null;
        Result<VideoProviderRef, Error> providerRefResult =
            await _providerRefRepository.GetByAsync(r => r.AssetId == query.VideoId, cancellationToken);
        if (providerRefResult.IsSuccess)
        {
            providerRef = providerRefResult.Value;
        }

        return asset.ToVideoResponse(providerRef);
    }
}
