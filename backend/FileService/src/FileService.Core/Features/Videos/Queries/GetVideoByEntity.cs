using Core.Abstractions;
using FileService.Contracts.Assets;
using FileService.Core.Repositories;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace FileService.Core.Features.Videos.Queries;

public sealed record GetVideoByEntityQuery(Guid EntityId, string EntityType) : IQuery;

public sealed class GetVideoByEntityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/videos/by-entity", async Task<EndpointResult<GetVideoResponse?>> (
                [FromQuery] Guid entityId,
                [FromQuery] string entityType,
                [FromServices] IQueryHandlerWithResult<GetVideoResponse?, GetVideoByEntityQuery> handler,
                CancellationToken token) => await handler.Handle(new GetVideoByEntityQuery(entityId, entityType), token))
            .RequirePermissions(PlatformPermissions.Videos.READ);
    }
}

public sealed class GetVideoByEntityHandler : IQueryHandlerWithResult<GetVideoResponse?, GetVideoByEntityQuery>
{
    private readonly IMediaAssetRepository _repository;
    private readonly IVideoProviderRefRepository _providerRefRepository;
    private readonly ITargetEntityAuthorization _targetAuthorization;

    public GetVideoByEntityHandler(
        IMediaAssetRepository repository,
        IVideoProviderRefRepository providerRefRepository,
        ITargetEntityAuthorization targetAuthorization)
    {
        _repository = repository;
        _providerRefRepository = providerRefRepository;
        _targetAuthorization = targetAuthorization;
    }

    public async Task<Result<GetVideoResponse?, Error>> Handle(
        GetVideoByEntityQuery query,
        CancellationToken cancellationToken)
    {
        Result<TargetEntity, Error> target = TargetEntity.Of(query.EntityType, query.EntityId);
        if (target.IsFailure)
            return target.Error;

        UnitResult<Error> targetAuthorization = await _targetAuthorization.AuthorizeManagerAsync(
            target.Value,
            cancellationToken);
        if (targetAuthorization.IsFailure)
            return targetAuthorization.Error;

        List<MediaAsset> assets = await _repository.GetManyByAsync(
            a => a.Kind == AssetKind.VIDEO
                 && a.TargetEntity != null
                 && a.TargetEntity.Type == query.EntityType
                 && a.TargetEntity.Id == query.EntityId
                 && a.ConfirmedBindingRevision > a.DetachedThroughBindingRevision
                 && a.Status != AssetStatus.DELETED
                 && a.Status != AssetStatus.DELETING,
            cancellationToken);

        MediaAsset? asset = assets
            .OrderByDescending(a => a.ConfirmedBindingRevision)
            .ThenByDescending(a => a.BindingRevision)
            .ThenByDescending(a => a.CreatedAt)
            .FirstOrDefault();

        if (asset is null)
        {
            return Result.Success<GetVideoResponse?, Error>(null);
        }

        VideoProviderRef? providerRef = null;
        Result<VideoProviderRef, Error> providerRefResult =
            await _providerRefRepository.GetByAsync(r => r.AssetId == asset.Id, cancellationToken);
        if (providerRefResult.IsSuccess)
        {
            providerRef = providerRefResult.Value;
        }

        return asset.ToVideoResponse(providerRef);
    }
}
