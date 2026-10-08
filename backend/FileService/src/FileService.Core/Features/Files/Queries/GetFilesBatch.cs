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

namespace FileService.Core.Features.Files.Queries;

public sealed record GetFilesBatchQuery(IReadOnlyList<Guid> Ids) : IQuery;

public sealed class GetFilesBatchEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/files/batch/", async Task<EndpointResult<List<GetFileResponse>>> (
                [FromQuery] Guid[] ids,
                [FromServices] IQueryHandlerWithResult<List<GetFileResponse>, GetFilesBatchQuery> handler,
                CancellationToken token) => await handler.Handle(new GetFilesBatchQuery(ids), token))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetFilesBatchHandler : IQueryHandlerWithResult<List<GetFileResponse>, GetFilesBatchQuery>
{
    private const int MAX_BATCH_SIZE = 50;

    private readonly IMediaAssetRepository _repository;
    private readonly FileContentUrlBuilder _contentUrlBuilder;

    public GetFilesBatchHandler(IMediaAssetRepository repository, FileContentUrlBuilder contentUrlBuilder)
    {
        _repository = repository;
        _contentUrlBuilder = contentUrlBuilder;
    }

    public async Task<Result<List<GetFileResponse>, Error>> Handle(
        GetFilesBatchQuery query, CancellationToken cancellationToken)
    {
        if (query.Ids.Count == 0)
            return new List<GetFileResponse>();

        if (query.Ids.Count > MAX_BATCH_SIZE)
            return GeneralErrors.ValueIsInvalid("ids");

        HashSet<Guid> uniqueIds = query.Ids.ToHashSet();

        List<MediaAsset> assets = await _repository.GetManyByAsync(
            a => uniqueIds.Contains(a.Id)
                 && a.Kind == AssetKind.FILE
                 && a.Status != AssetStatus.DELETED
                 && a.Status != AssetStatus.DELETING,
            cancellationToken);

        List<GetFileResponse> response = assets
            .Select(a => a.ToFileResponse(_contentUrlBuilder.Build(a.Id)))
            .ToList();

        Dictionary<Guid, int> idOrder = query.Ids
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i);
        response = [.. response.OrderBy(r => idOrder.GetValueOrDefault(r.Id, int.MaxValue))];

        return response;
    }
}
