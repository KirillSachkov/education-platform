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

namespace FileService.Core.Features.AssetRegistry.UseCases;

public sealed record DetachAssetCommand(Guid AssetId) : ICommand;

public sealed class DetachAssetEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/files/{assetId:guid}/detach", async Task<EndpointResult> (
                [FromRoute] Guid assetId,
                [FromServices] ICommandHandler<DetachAssetCommand> handler,
                CancellationToken token) => await handler.Handle(new DetachAssetCommand(assetId), token))
            .RequirePermissions(PlatformPermissions.Files.MANAGE);
    }
}

/// <summary>
///     Sync-эндпоинт для явного удаления.
///     Idempotent: повторный вызов на уже удаляемом/удалённом ассете — no-op.
///     Asset переводится в DELETING, фактическое удаление из S3/Kinescope делает
///     <c>AssetRetentionService</c>.
///
///     Publishes <see cref="FileAssetDeleted"/> in the same transaction — downstream
///     consumers (AuthService avatar
///     cleanup, ECS material/course media references) rely on this event to cascade
///     their own deletion. Mirrors <c>DeleteFile</c>/<c>DeleteVideo</c>.
/// </summary>
public sealed class DetachAssetHandler : ICommandHandler<DetachAssetCommand>
{
    private readonly IMediaAssetRepository _repository;
    private readonly IOutboxService _outbox;
    private readonly ITransactionManager _transactionManager;
    private readonly UserScopedData _user;
    private readonly ITargetEntityAuthorization _targetAuthorization;
    private readonly ILogger<DetachAssetHandler> _logger;

    public DetachAssetHandler(
        IMediaAssetRepository repository,
        IOutboxService outbox,
        ITransactionManager transactionManager,
        UserScopedData user,
        ITargetEntityAuthorization targetAuthorization,
        ILogger<DetachAssetHandler> logger)
    {
        _repository = repository;
        _outbox = outbox;
        _transactionManager = transactionManager;
        _user = user;
        _targetAuthorization = targetAuthorization;
        _logger = logger;
    }

    public async Task<UnitResult<Error>> Handle(DetachAssetCommand command, CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult = await _repository.GetByAsync(
            a => a.Id == command.AssetId, cancellationToken);
        if (assetResult.IsFailure)
            return assetResult.Error;

        MediaAsset asset = assetResult.Value;

        bool isPrivileged = _user.IsAdmin;
        if (!isPrivileged && asset.UploadedByUserId != _user.UserId)
            return Error.Authorization("asset.detach.not.owner", "Нет доступа к данному ресурсу");

        if (asset.TargetEntity is not null)
        {
            UnitResult<Error> targetAuthorization = await _targetAuthorization.AuthorizeManagerAsync(
                asset.TargetEntity,
                cancellationToken);
            if (targetAuthorization.IsFailure)
                return targetAuthorization.Error;
        }

        if (asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
            return UnitResult.Success<Error>();

        UnitResult<Error> deleteResult = asset.RequestDelete();
        if (deleteResult.IsFailure)
            return deleteResult.Error;

        await _outbox.PublishAsync(new FileAssetDeleted(
            asset.Id,
            asset.Kind.ToString().ToLowerInvariant(),
            asset.UsageType.ToApiString(),
            asset.TargetEntity?.Id,
            asset.TargetEntity?.Type,
            asset.GetDeletionBindingRevision()));

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
            return saveResult.Error;

        _logger.LogInformation("Asset {AssetId} marked for deletion via sync detach", asset.Id);

        return UnitResult.Success<Error>();
    }
}
