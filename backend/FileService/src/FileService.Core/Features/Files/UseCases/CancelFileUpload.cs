using Core.Abstractions;
using Core.Database;
using FileService.Core.FilesStorage;
using FileService.Core.Repositories;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace FileService.Core.Features.Files.UseCases;

public sealed record CancelFileUploadCommand(Guid AssetId) : ICommand;

public sealed class CancelFileUploadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/files/{fileId:guid}/cancel", async Task<EndpointResult> (
                [FromRoute] Guid fileId,
                [FromServices] ICommandHandler<CancelFileUploadCommand> handler,
                CancellationToken token) => await handler.Handle(new CancelFileUploadCommand(fileId), token))
            .RequirePermissions(PlatformPermissions.Files.UPLOAD);
    }
}

public sealed class CancelFileUploadHandler : ICommandHandler<CancelFileUploadCommand>
{
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IFileStorageRefRepository _fileStorageRefRepository;
    private readonly IObjectStorageProvider _objectStorageProvider;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;

    public CancelFileUploadHandler(
        IMediaAssetRepository assetRepository,
        IFileStorageRefRepository fileStorageRefRepository,
        IObjectStorageProvider objectStorageProvider,
        ITransactionManager transactionManager,
        UserScopedData user)
    {
        _assetRepository = assetRepository;
        _fileStorageRefRepository = fileStorageRefRepository;
        _objectStorageProvider = objectStorageProvider;
        _transactionManager = transactionManager;
        _user = user;
    }

    public async Task<UnitResult<Error>> Handle(CancelFileUploadCommand command, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult =
            await _assetRepository.GetByAsync(a => a.Id == command.AssetId, cancellationToken);
        if (assetResult.IsFailure)
        {
            return assetResult.Error;
        }

        MediaAsset asset = assetResult.Value;

        bool isPrivileged = _user.IsAdmin;
        if (!isPrivileged && asset.UploadedByUserId != _user.UserId)
        {
            return Error.Authorization("file.upload.not.owner", "Нет доступа к данной загрузке");
        }

        if (asset.Kind != AssetKind.FILE)
        {
            return Error.Validation("asset.kind.invalid", "Отменить можно только загрузку файлов");
        }

        if (asset.Status != AssetStatus.PENDING_UPLOAD &&
            asset.Status != AssetStatus.FAILED &&
            !asset.IsTemporary)
        {
            return Error.Validation("asset.cancel.invalid", "Ресурс не может быть отменён");
        }

        Result<FileStorageRef, Error> storageRefResult =
            await _fileStorageRefRepository.GetByAsync(r => r.AssetId == asset.Id, cancellationToken);
        if (storageRefResult.IsSuccess)
        {
            await _objectStorageProvider.DeleteAsync(storageRefResult.Value.StorageKey.Value, cancellationToken);
        }

        UnitResult<Error> requestDeleteResult = asset.RequestDelete();
        if (requestDeleteResult.IsFailure)
        {
            return requestDeleteResult.Error;
        }

        UnitResult<Error> markDeletedResult = asset.MarkDeleted();
        if (markDeletedResult.IsFailure)
        {
            return markDeletedResult.Error;
        }

        return await _transactionManager.SaveChangesAsync(cancellationToken);
    }
}
