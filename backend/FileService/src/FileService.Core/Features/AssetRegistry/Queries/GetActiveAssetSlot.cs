using Core.Abstractions;
using FileService.Contracts.Assets;
using FileService.Core.Repositories;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;

namespace FileService.Core.Features.AssetRegistry.Queries;

public sealed record GetActiveAssetSlotQuery(
    string EntityType,
    Guid EntityId,
    string UsageType) : IQuery;

public sealed class GetActiveAssetSlotEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/assets/active-slot/", async Task<EndpointResult<GetActiveAssetSlotResponse?>> (
                [FromQuery] string entityType,
                [FromQuery] Guid entityId,
                [FromQuery] string usageType,
                [FromServices] IQueryHandlerWithResult<GetActiveAssetSlotResponse?, GetActiveAssetSlotQuery> handler,
                CancellationToken token) => await handler.Handle(
                new GetActiveAssetSlotQuery(entityType, entityId, usageType),
                token))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetActiveAssetSlotHandler
    : IQueryHandlerWithResult<GetActiveAssetSlotResponse?, GetActiveAssetSlotQuery>
{
    private readonly IMediaAssetRepository _repository;

    public GetActiveAssetSlotHandler(IMediaAssetRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<GetActiveAssetSlotResponse?, Error>> Handle(
        GetActiveAssetSlotQuery query,
        CancellationToken cancellationToken)
    {
        Result<AssetUsageType, Error> usageType = AssetUsageTypeExtensions.FromString(query.UsageType);
        if (usageType.IsFailure)
            return usageType.Error;

        Result<TargetEntity, Error> target = TargetEntity.Of(query.EntityType, query.EntityId);
        if (target.IsFailure)
            return target.Error;

        MediaAsset? active = await _repository.GetActiveSlotAssetAsync(
            target.Value.Type,
            target.Value.Id,
            usageType.Value,
            cancellationToken);
        return active is null
            ? null
            : new GetActiveAssetSlotResponse(active.Id);
    }
}
