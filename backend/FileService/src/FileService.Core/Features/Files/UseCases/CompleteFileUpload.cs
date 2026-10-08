using Core.Abstractions;
using Core.Database;
using FileService.Contracts.Assets;
using FileService.Core.Database;
using FileService.Core.FilesStorage;
using FileService.Core.Messaging;
using FileService.Core.Repositories;
using FileService.Core.Services;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using FluentValidation;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Features.Files.UseCases;

public sealed record CompleteFileUploadCommand(Guid AssetId, CompleteFileUploadRequest Request) : ICommand;

public sealed class CompleteFileUploadRequestValidator : AbstractValidator<CompleteFileUploadRequest>
{
    public CompleteFileUploadRequestValidator()
    {
        RuleFor(x => x.Checksum)
            .MaximumLength(256)
            .When(x => x.Checksum is not null);
    }
}

public sealed class CompleteFileUploadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/files/{fileId:guid}/complete", async Task<EndpointResult<CompleteFileUploadResponse>> (
                    [FromRoute] Guid fileId,
                    [FromBody] CompleteFileUploadRequest request,
                    [FromServices] ICommandHandler<CompleteFileUploadResponse, CompleteFileUploadCommand> handler,
                    CancellationToken token) =>
                await handler.Handle(new CompleteFileUploadCommand(fileId, request), token))
            .RequirePermissions(PlatformPermissions.Files.UPLOAD);
    }
}

public sealed class CompleteFileUploadHandler
    : ICommandHandler<CompleteFileUploadResponse, CompleteFileUploadCommand>
{
    private readonly IObjectStorageProvider _objectStorageProvider;
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IFileStorageRefRepository _fileStorageRefRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly AssetBindEventPublisher _bindEventPublisher;
    private readonly FileContentUrlBuilder _contentUrlBuilder;
    private readonly ITargetEntityAuthorization _targetAuthorization;
    private readonly UserScopedData _user;
    private readonly ILogger<CompleteFileUploadHandler> _logger;

    public CompleteFileUploadHandler(
        IObjectStorageProvider objectStorageProvider,
        IMediaAssetRepository assetRepository,
        IFileStorageRefRepository fileStorageRefRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        AssetBindEventPublisher bindEventPublisher,
        FileContentUrlBuilder contentUrlBuilder,
        ITargetEntityAuthorization targetAuthorization,
        UserScopedData user,
        ILogger<CompleteFileUploadHandler> logger)
    {
        _objectStorageProvider = objectStorageProvider;
        _assetRepository = assetRepository;
        _fileStorageRefRepository = fileStorageRefRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _bindEventPublisher = bindEventPublisher;
        _contentUrlBuilder = contentUrlBuilder;
        _targetAuthorization = targetAuthorization;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<CompleteFileUploadResponse, Error>> Handle(
        CompleteFileUploadCommand command,
        CancellationToken cancellationToken)
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
            return Error.Validation("asset.kind.invalid", "Завершить можно только файловые ресурсы");
        }

        if (asset.Status == AssetStatus.READY)
        {
            return new CompleteFileUploadResponse(
                asset.Id,
                asset.Status.ToApiString(),
                _contentUrlBuilder.Build(asset.Id));
        }

        if (asset.Status == AssetStatus.FAILED)
        {
            return Error.Conflict("asset.failed", "Ресурс уже находится в состоянии ошибки");
        }

        if (asset.Status is AssetStatus.DELETING or AssetStatus.DELETED)
        {
            return Error.Conflict("asset.deleted", "Ресурс уже удалён или в процессе удаления");
        }

        if (asset.Status != AssetStatus.PENDING_UPLOAD)
        {
            return Error.Validation("asset.status.invalid", "Ресурс не ожидает загрузки");
        }

        Result<FileStorageRef, Error> storageRefResult =
            await _fileStorageRefRepository.GetByAsync(r => r.AssetId == asset.Id, cancellationToken);
        if (storageRefResult.IsFailure)
        {
            return storageRefResult.Error;
        }

        FileStorageRef storageRef = storageRefResult.Value;
        Result<ObjectStorageObjectMetadata, Error> metadataResult = await _objectStorageProvider.GetMetadataAsync(
            storageRef.StorageKey.Value,
            cancellationToken);

        if (metadataResult.IsFailure)
        {
            asset.MarkFailed("Uploaded object was not found");
            UnitResult<Error> saveFailedResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveFailedResult.IsFailure)
                return saveFailedResult.Error;
            return metadataResult.Error;
        }

        string? requestedChecksum = NormalizeChecksum(command.Request.Checksum);
        storageRef.UpdateMetadata(new FileStorageMetadata
        {
            Checksum = requestedChecksum,
            ETag = NormalizeChecksum(metadataResult.Value.ETag),
            DetectedContentType = metadataResult.Value.ContentType?.Trim().ToLowerInvariant(),
        });

        if (metadataResult.Value.Size != asset.Size)
        {
            // Integrity failure: potential content-type spoofing or tampered upload. Worth
            // logging at Warning for ops visibility (beyond the MarkFailed domain record).
            _logger.LogWarning(
                "Upload integrity failure for asset {AssetId}: size mismatch (expected={Expected}, actual={Actual})",
                asset.Id, asset.Size, metadataResult.Value.Size);
            asset.MarkFailed("Uploaded object size does not match expected size");
            UnitResult<Error> saveFailedResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveFailedResult.IsFailure)
                return saveFailedResult.Error;
            return Error.Validation("asset.size.mismatch", "Размер загруженного объекта не совпадает");
        }

        if (!string.IsNullOrWhiteSpace(metadataResult.Value.ContentType) &&
            !string.Equals(metadataResult.Value.ContentType, asset.ContentType.Value,
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Upload integrity failure for asset {AssetId}: content-type mismatch (expected={Expected}, actual={Actual})",
                asset.Id, asset.ContentType.Value, metadataResult.Value.ContentType);
            asset.MarkFailed("Uploaded object content type does not match expected content type");
            UnitResult<Error> saveFailedResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveFailedResult.IsFailure)
                return saveFailedResult.Error;
            return Error.Validation("asset.contentType.mismatch", "Тип контента загруженного объекта не совпадает");
        }

        if (!string.IsNullOrWhiteSpace(requestedChecksum) &&
            !string.Equals(NormalizeChecksum(metadataResult.Value.ETag), requestedChecksum,
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Upload integrity failure for asset {AssetId}: checksum mismatch",
                asset.Id);
            asset.MarkFailed("Uploaded object checksum does not match expected checksum");
            UnitResult<Error> saveFailedResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveFailedResult.IsFailure)
                return saveFailedResult.Error;
            return Error.Validation("asset.checksum.mismatch", "Контрольная сумма загруженного объекта не совпадает");
        }

        UnitResult<Error> markReadyResult = asset.MarkReady();
        if (markReadyResult.IsFailure)
        {
            return markReadyResult.Error;
        }

        // #646: generate responsive variants asynchronously (off the hot upload path).
        // Outbox-published here; FileService self-consumes on a local Wolverine queue.
        if (asset.IsImageVariantEligible())
        {
            await _outbox.PublishAsync(new GenerateImageVariants(asset.Id));
        }

        if (asset.TargetEntity is not null)
        {
            UnitResult<Error> targetAuthorization = await _targetAuthorization.AuthorizeAsync(
                asset.TargetEntity,
                cancellationToken: cancellationToken);
            if (targetAuthorization.IsFailure)
                return targetAuthorization.Error;

            Result<long, Error> binding = await _bindEventPublisher.PublishAsync(
                asset,
                asset.TargetEntity,
                advanceBindingRevision: AssetUsagePolicyCatalog.IsSingleAssetPerEntity(asset.UsageType),
                cancellationToken: cancellationToken);
            if (binding.IsFailure)
                return binding.Error;
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "File upload completed: AssetId={AssetId}, UsageType={UsageType}",
            asset.Id, asset.UsageType);

        return new CompleteFileUploadResponse(
            asset.Id,
            asset.Status.ToApiString(),
            _contentUrlBuilder.Build(asset.Id));
    }

    private static string? NormalizeChecksum(string? checksum)
    {
        if (string.IsNullOrWhiteSpace(checksum))
        {
            return null;
        }

        return checksum.Trim().Trim('"');
    }

}
