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

namespace FileService.Core.Features.Files.Queries;

public sealed record GetFileQuery(Guid FileId, bool Internal = false) : IQuery;

public sealed class GetFileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/files/{fileId:guid}", async Task<EndpointResult<GetFileResponse?>> (
                [FromRoute] Guid fileId,
                [FromServices] IQueryHandlerWithResult<GetFileResponse?, GetFileQuery> handler,
                CancellationToken token) => await handler.Handle(new GetFileQuery(fileId), token))
            .RequirePermissions(PlatformPermissions.Files.MANAGE);
    }
}

public sealed class GetFileInternalEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/internal/files/{fileId:guid}/", async Task<EndpointResult<GetFileResponse?>> (
                [FromRoute] Guid fileId,
                [FromServices] IQueryHandlerWithResult<GetFileResponse?, GetFileQuery> handler,
                CancellationToken token) => await handler.Handle(new GetFileQuery(fileId, Internal: true), token))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class GetFileHandler : IQueryHandlerWithResult<GetFileResponse?, GetFileQuery>
{
    private readonly IMediaAssetRepository _repository;
    private readonly FileContentUrlBuilder _contentUrlBuilder;
    private readonly UserScopedData _user;

    public GetFileHandler(
        IMediaAssetRepository repository,
        FileContentUrlBuilder contentUrlBuilder,
        UserScopedData user)
    {
        _repository = repository;
        _contentUrlBuilder = contentUrlBuilder;
        _user = user;
    }

    public async Task<Result<GetFileResponse?, Error>> Handle(GetFileQuery query, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult =
            await _repository.GetByAsync(a => a.Id == query.FileId, cancellationToken);
        if (assetResult.IsFailure)
        {
            if (assetResult.Error.Type == ErrorType.NOT_FOUND)
            {
                return Result.Success<GetFileResponse?, Error>(null);
            }

            return assetResult.Error;
        }

        MediaAsset asset = assetResult.Value;
        if (asset.Kind != AssetKind.FILE)
        {
            return Result.Success<GetFileResponse?, Error>(null);
        }

        if (query.Internal)
        {
            if (asset.IsTemporary || asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
                return Result.Success<GetFileResponse?, Error>(null);
        }
        else
        {
            if (!_user.IsAdmin && asset.UploadedByUserId != _user.UserId)
                return Error.Authorization("file.not.owner", "Нет доступа к данному файлу");

            if (asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
                return Result.Success<GetFileResponse?, Error>(null);
        }

        return asset.ToFileResponse(_contentUrlBuilder.Build(asset.Id));
    }
}
