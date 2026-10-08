using Core.Abstractions;
using FileService.Contracts.Assets;
using FileService.Core.Repositories;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace FileService.Core.Features.Videos.Queries;

public sealed record GetVideosBatchQuery(IReadOnlyList<Guid> Ids) : IQuery;

public sealed class GetVideosBatchEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/videos/batch/", async Task<EndpointResult<List<GetPublicVideoResponse>>> (
                [FromQuery] Guid[] ids,
                [FromServices] IQueryHandlerWithResult<List<GetPublicVideoResponse>, GetVideosBatchQuery> handler,
                CancellationToken token) => await handler.Handle(new GetVideosBatchQuery(ids), token))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetVideosBatchHandler : IQueryHandlerWithResult<List<GetPublicVideoResponse>, GetVideosBatchQuery>
{
    private const int MAX_BATCH_SIZE = 50;

    private readonly IMediaAssetRepository _repository;
    private readonly IVideoProviderRefRepository _providerRefRepository;

    public GetVideosBatchHandler(
        IMediaAssetRepository repository,
        IVideoProviderRefRepository providerRefRepository)
    {
        _repository = repository;
        _providerRefRepository = providerRefRepository;
    }

    public async Task<Result<List<GetPublicVideoResponse>, Error>> Handle(
        GetVideosBatchQuery query, CancellationToken cancellationToken)
    {
        if (query.Ids.Count == 0)
            return new List<GetPublicVideoResponse>();

        if (query.Ids.Count > MAX_BATCH_SIZE)
            return GeneralErrors.ValueIsInvalid("ids");

        HashSet<Guid> uniqueIds = query.Ids.ToHashSet();

        List<MediaAsset> assets = await _repository.GetManyByAsync(
            a => uniqueIds.Contains(a.Id)
                 && a.Kind == AssetKind.VIDEO
                 && a.Status == AssetStatus.READY
                 && !a.IsTemporary,
            cancellationToken);

        Guid[] assetIds = assets.Select(a => a.Id).ToArray();
        IReadOnlyDictionary<Guid, VideoProviderRef> providerRefs =
            await _providerRefRepository.GetByAssetIdsAsync(assetIds, cancellationToken);

        List<GetPublicVideoResponse> response = assets
            .Select(asset =>
            {
                VideoProviderRef? providerRef = providerRefs.GetValueOrDefault(asset.Id);
                return new GetPublicVideoResponse(
                    asset.Id,
                    providerRef?.Metadata?.ThumbnailUrl,
                    providerRef?.Metadata?.DurationSeconds);
            })
            .ToList();

        Dictionary<Guid, int> idOrder = query.Ids
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i);
        response = [.. response.OrderBy(r => idOrder.GetValueOrDefault(r.Id, int.MaxValue))];

        return response;
    }
}
