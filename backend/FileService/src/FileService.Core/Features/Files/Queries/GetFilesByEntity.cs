using Core.Abstractions;
using FileService.Contracts.Assets;
using FileService.Core.Repositories;
using FileService.Core.Services;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace FileService.Core.Features.Files.Queries;

public sealed record GetFilesByEntityQuery(Guid EntityId, string EntityType) : IQuery;

public sealed class GetFilesByEntityEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/files/by-entity", async Task<EndpointResult<List<GetFileResponse>>> (
                [FromQuery] Guid entityId,
                [FromQuery] string entityType,
                [FromServices] IQueryHandlerWithResult<List<GetFileResponse>, GetFilesByEntityQuery> handler,
                CancellationToken token) => await handler.Handle(new GetFilesByEntityQuery(entityId, entityType), token))
            .RequirePermissions(PlatformPermissions.Files.MANAGE);
    }
}

public sealed class GetFilesByEntityHandler : IQueryHandlerWithResult<List<GetFileResponse>, GetFilesByEntityQuery>
{
    // Safety cap to prevent unbounded result sets for entities that have
    // accumulated many attached files (e.g., markdown assets over time).
    // Clients should not rely on more than this many results — if pagination
    // becomes necessary, introduce explicit cursor-based pagination.
    private const int MAX_RESULTS = 200;

    private readonly IMediaAssetRepository _repository;
    private readonly FileContentUrlBuilder _contentUrlBuilder;
    private readonly ITargetEntityAuthorization _targetAuthorization;

    public GetFilesByEntityHandler(
        IMediaAssetRepository repository,
        FileContentUrlBuilder contentUrlBuilder,
        ITargetEntityAuthorization targetAuthorization)
    {
        _repository = repository;
        _contentUrlBuilder = contentUrlBuilder;
        _targetAuthorization = targetAuthorization;
    }

    public async Task<Result<List<GetFileResponse>, Error>> Handle(
        GetFilesByEntityQuery query, CancellationToken cancellationToken)
    {
        Result<TargetEntity, Error> target = TargetEntity.Of(query.EntityType, query.EntityId);
        if (target.IsFailure)
            return target.Error;

        UnitResult<Error> targetAuthorization = await _targetAuthorization.AuthorizeManagerAsync(
            target.Value,
            cancellationToken);
        if (targetAuthorization.IsFailure)
            return targetAuthorization.Error;

        List<MediaAsset> assets = await _repository.GetManyByOrderedAsync(
            a => a.Kind == AssetKind.FILE
                 && a.TargetEntity != null
                 && a.TargetEntity.Type == query.EntityType
                 && a.TargetEntity.Id == query.EntityId
                 && a.Status != AssetStatus.DELETED
                 && a.Status != AssetStatus.DELETING,
            a => a.CreatedAt,
            MAX_RESULTS,
            cancellationToken);

        List<GetFileResponse> response = assets
            .Select(a => a.ToFileResponse(_contentUrlBuilder.Build(a.Id)))
            .ToList();

        return response;
    }
}
