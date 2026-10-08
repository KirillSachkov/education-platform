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

namespace FileService.Core.Features.Videos.Queries;

public sealed record GetVideoProcessingSourceQuery(Guid VideoId) : IQuery;

public sealed class GetVideoProcessingSourceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/videos/{videoId:guid}/processing-source/", async Task<EndpointResult<GetVideoProcessingSourceResponse?>> (
                [FromRoute] Guid videoId,
                [FromServices] IQueryHandlerWithResult<GetVideoProcessingSourceResponse?, GetVideoProcessingSourceQuery> handler,
                CancellationToken token) => await handler.Handle(new GetVideoProcessingSourceQuery(videoId), token))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetVideoProcessingSourceHandler
    : IQueryHandlerWithResult<GetVideoProcessingSourceResponse?, GetVideoProcessingSourceQuery>
{
    private readonly IMediaAssetRepository _repository;
    private readonly IVideoProviderRefRepository _providerRefRepository;
    private readonly IVideoProvider _videoProvider;

    public GetVideoProcessingSourceHandler(
        IMediaAssetRepository repository,
        IVideoProviderRefRepository providerRefRepository,
        IVideoProvider videoProvider)
    {
        _repository = repository;
        _providerRefRepository = providerRefRepository;
        _videoProvider = videoProvider;
    }

    public async Task<Result<GetVideoProcessingSourceResponse?, Error>> Handle(
        GetVideoProcessingSourceQuery query,
        CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult =
            await _repository.GetByAsync(x => x.Id == query.VideoId, cancellationToken);
        if (assetResult.IsFailure)
        {
            if (assetResult.Error.Type == ErrorType.NOT_FOUND)
                return Result.Success<GetVideoProcessingSourceResponse?, Error>(null);

            return assetResult.Error;
        }

        MediaAsset asset = assetResult.Value;
        if (asset.Kind != AssetKind.VIDEO)
            return Result.Success<GetVideoProcessingSourceResponse?, Error>(null);

        if (asset.Status != AssetStatus.READY)
        {
            return Error.Validation("video.not_ready", "Видео ещё не готово к обработке");
        }

        Result<VideoProviderRef, Error> providerRefResult =
            await _providerRefRepository.GetByAsync(x => x.AssetId == query.VideoId, cancellationToken);

        if (providerRefResult.IsFailure)
        {
            if (providerRefResult.Error.Type == ErrorType.NOT_FOUND)
                return Result.Success<GetVideoProcessingSourceResponse?, Error>(null);

            return providerRefResult.Error;
        }

        Result<VideoProcessingSourceInfo, Error> sourceResult =
            await _videoProvider.GetProcessingSourceAsync(providerRefResult.Value.ExternalAssetId, cancellationToken);

        if (sourceResult.IsFailure)
            return sourceResult.Error;

        VideoProcessingSourceInfo source = sourceResult.Value;

        return new GetVideoProcessingSourceResponse(
            query.VideoId,
            asset.Status.ToApiString(),
            providerRefResult.Value.Version,
            source.DurationSeconds ?? providerRefResult.Value.Metadata?.DurationSeconds,
            source.SourceType,
            source.Url,
            source.ExpiresAt,
            asset.UploadedByUserId);
    }
}
