using Core.Abstractions;
using Core.Database;
using FileService.Core.Database;
using FileService.Core.Repositories;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Features.Videos.UseCases;

public sealed record DeleteVideoCommand(Guid AssetId) : ICommand;

public sealed class DeleteVideoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapDelete("/videos/{videoId:guid}", async Task<EndpointResult> (
                    [FromRoute] Guid videoId,
                    [FromServices] ICommandHandler<DeleteVideoCommand> handler,
                    CancellationToken token) =>
                await handler.Handle(new DeleteVideoCommand(videoId), token))
            .RequirePermissions(PlatformPermissions.Videos.MANAGE);
    }
}

/// <summary>
///     Two-phase delete, phase 1: transitions the asset to DELETING and publishes
///     <see cref="FileAssetDeleted"/> via the durable outbox. The physical Kinescope
///     delete and provider-ref cleanup are performed later by
///     <see cref="Services.AssetRegistry.AssetRetentionService"/>, which also
///     transitions the asset to DELETED on success. This guarantees the integration
///     event is always published, even if the external delete fails.
/// </summary>
public sealed class DeleteVideoHandler : ICommandHandler<DeleteVideoCommand>
{
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly ITargetEntityAuthorization _targetAuthorization;
    private readonly ILogger<DeleteVideoHandler> _logger;

    public DeleteVideoHandler(
        IMediaAssetRepository assetRepository,
        IOutboxService outbox,
        ITransactionManager transactionManager,
        UserScopedData user,
        ITargetEntityAuthorization targetAuthorization,
        ILogger<DeleteVideoHandler> logger)
    {
        _assetRepository = assetRepository;
        _outbox = outbox;
        _transactionManager = transactionManager;
        _user = user;
        _targetAuthorization = targetAuthorization;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(DeleteVideoCommand command, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult =
            await _assetRepository.GetByAsync(a => a.Id == command.AssetId, cancellationToken);
        if (assetResult.IsFailure)
        {
            return assetResult.Error;
        }

        MediaAsset asset = assetResult.Value;
        if (asset.Kind != AssetKind.VIDEO)
        {
            return GeneralErrors.NotFound(command.AssetId);
        }

        bool isPrivileged = _user.IsAdmin;
        if (!isPrivileged && asset.UploadedByUserId != _user.UserId)
        {
            return Error.Authorization("video.delete.not.owner", "Нет доступа к данному ресурсу");
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
            _logger.LogInformation("Video delete requested: AssetId={AssetId}", command.AssetId);
        }

        return saveResult;
    }
}
