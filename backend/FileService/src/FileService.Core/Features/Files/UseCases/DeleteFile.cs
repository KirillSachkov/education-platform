using Core.Abstractions;
using Core.Database;
using FileService.Core.Caching;
using FileService.Core.Database;
using FileService.Core.Repositories;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Caching.Hybrid;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Features.Files.UseCases;

public sealed record DeleteFileCommand(Guid AssetId) : ICommand;

public sealed class DeleteFileEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/files/{fileId:guid}", async Task<EndpointResult> (
                    [FromRoute] Guid fileId,
                    [FromServices] ICommandHandler<DeleteFileCommand> handler,
                    CancellationToken token) =>
                await handler.Handle(new DeleteFileCommand(fileId), token))
            .RequirePermissions(PlatformPermissions.Files.MANAGE);
    }
}

/// <summary>
///     Two-phase delete, phase 1: transitions the asset to DELETING and publishes
///     <see cref="FileAssetDeleted"/> via the durable outbox. The physical S3 delete
///     is performed later by <see cref="Services.AssetRegistry.AssetRetentionService"/>,
///     which also transitions the asset to DELETED on success. This guarantees the
///     integration event is always published, even if the external delete fails.
/// </summary>
public sealed class DeleteFileHandler : ICommandHandler<DeleteFileCommand>
{
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactionManager;
    private readonly HybridCache _cache;
    private readonly UserScopedData _user;
    private readonly ITargetEntityAuthorization _targetAuthorization;
    private readonly ILogger<DeleteFileHandler> _logger;

    public DeleteFileHandler(
        IMediaAssetRepository assetRepository,
        IOutboxService outbox,
        ITransactionManager transactionManager,
        HybridCache cache,
        UserScopedData user,
        ITargetEntityAuthorization targetAuthorization,
        ILogger<DeleteFileHandler> logger)
    {
        _assetRepository = assetRepository;
        _outbox = outbox;
        _transactionManager = transactionManager;
        _cache = cache;
        _user = user;
        _targetAuthorization = targetAuthorization;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(DeleteFileCommand command, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult =
            await _assetRepository.GetByAsync(a => a.Id == command.AssetId, cancellationToken);
        if (assetResult.IsFailure)
        {
            return assetResult.Error;
        }

        MediaAsset asset = assetResult.Value;
        if (asset.Kind != AssetKind.FILE)
        {
            return GeneralErrors.NotFound(command.AssetId);
        }

        bool isPrivileged = _user.IsAdmin;
        if (!isPrivileged && asset.UploadedByUserId != _user.UserId)
        {
            return Error.Authorization("file.delete.not.owner", "Нет доступа к данному ресурсу");
        }

        if (asset.TargetEntity is not null)
        {
            UnitResult<Error> targetAuthorization = await _targetAuthorization.AuthorizeManagerAsync(
                asset.TargetEntity,
                cancellationToken);
            if (targetAuthorization.IsFailure)
                return targetAuthorization.Error;
        }

        UnitResult<Error> requestDeleteResult = asset.RequestDelete();
        if (requestDeleteResult.IsFailure)
        {
            return requestDeleteResult.Error;
        }

        await _outbox.PublishAsync(new FileAssetDeleted(
            asset.Id,
            asset.Kind.ToString().ToLowerInvariant(),
            asset.UsageType.ToApiString(),
            asset.TargetEntity?.Id,
            asset.TargetEntity?.Type,
            asset.GetDeletionBindingRevision()));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);

        if (saveResult.IsSuccess)
        {
            // Invalidate all cached presigned URLs for this file: the legacy global key
            // and every per-user key are all tagged with the same file-scoped tag, so
            // a single tag removal clears both variants.
            await _cache.RemoveByTagAsync(CacheKeys.FileDownloadUrl.TagById(command.AssetId), cancellationToken);
            await _cache.RemoveAsync(CacheKeys.FileDownloadUrl.ById(command.AssetId), cancellationToken);
            _logger.LogInformation("File delete requested: AssetId={AssetId}", command.AssetId);
        }

        return saveResult;
    }
}
