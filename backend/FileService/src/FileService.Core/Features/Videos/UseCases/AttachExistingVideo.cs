using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FileService.Contracts.Assets;
using FileService.Core.Database;
using FileService.Core.Features.AssetRegistry;
using FileService.Core.Repositories;
using FileService.Core.Services;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Features.Videos.UseCases;

public sealed record AttachExistingVideoCommand(AttachExistingVideoRequest Request) : ICommand;

public sealed class AttachExistingVideoEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/videos/attach-existing", async Task<EndpointResult<AttachExistingVideoResponse>> (
                    [FromBody] AttachExistingVideoRequest request,
                    [FromServices] ICommandHandler<AttachExistingVideoResponse, AttachExistingVideoCommand> handler,
                    CancellationToken token) =>
                await handler.Handle(new AttachExistingVideoCommand(request), token))
            // Historical provider ownership cannot be reconstructed until #737
            // inventories/backfills Kinescope. Keep manual adoption admin-only so
            // an author cannot claim a purged foreign provider id.
            .RequireAnyRole(PlatformRoles.ADMIN);
    }
}

public sealed class AttachExistingVideoRequestValidator : AbstractValidator<AttachExistingVideoRequest>
{
    public AttachExistingVideoRequestValidator()
    {
        RuleFor(x => x.ExternalVideoId)
            .NotEmpty().WithError(GeneralErrors.ValueIsRequired("externalVideoId"));
        RuleFor(x => x.UsageType)
            .NotEmpty().WithError(GeneralErrors.ValueIsRequired("usageType"))
            .MustBeValueObject(x => AssetUsageTypeExtensions.FromString(x, "usageType"));
        RuleFor(x => x)
            .Must(x => x.TargetEntity is not null || x.DraftId.HasValue)
            .WithError(GeneralErrors.ValueIsRequired("targetEntity"));
    }
}

public sealed class AttachExistingVideoHandler
    : ICommandHandler<AttachExistingVideoResponse, AttachExistingVideoCommand>
{
    private readonly IVideoProvider _videoProvider;
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IVideoProviderRefRepository _videoProviderRefRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly AssetBindEventPublisher _bindEventPublisher;
    private readonly IValidator<AttachExistingVideoRequest> _validator;
    private readonly TargetEntityOptions _targetEntityOptions;
    private readonly ITargetEntityAuthorization _targetAuthorization;
    private readonly UserScopedData _user;

    public AttachExistingVideoHandler(
        IVideoProvider videoProvider,
        IMediaAssetRepository assetRepository,
        IVideoProviderRefRepository videoProviderRefRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        AssetBindEventPublisher bindEventPublisher,
        IValidator<AttachExistingVideoRequest> validator,
        IOptions<TargetEntityOptions> targetEntityOptions,
        ITargetEntityAuthorization targetAuthorization,
        UserScopedData user)
    {
        _videoProvider = videoProvider;
        _assetRepository = assetRepository;
        _videoProviderRefRepository = videoProviderRefRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _bindEventPublisher = bindEventPublisher;
        _validator = validator;
        _targetEntityOptions = targetEntityOptions.Value;
        _targetAuthorization = targetAuthorization;
        _user = user;
    }

    public async Task<Result<AttachExistingVideoResponse, Error>> Handle(
        AttachExistingVideoCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command.Request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return validationResult.ToError();
        }

        Result<AssetUsageType, Error> usageTypeResult = AssetUsageTypeExtensions.FromString(command.Request.UsageType);
        if (usageTypeResult.IsFailure)
        {
            return usageTypeResult.Error;
        }

        AssetUsageType usageType = usageTypeResult.Value;

        Result<AssetUsagePolicy, Error> policyResult = AssetUsagePolicyCatalog.Get(usageType);
        if (policyResult.IsFailure)
        {
            return policyResult.Error;
        }

        if (policyResult.Value.Kind != AssetKind.VIDEO)
        {
            return Error.Validation("usageType.invalid", "Тип использования должен быть видео");
        }

        TargetEntity? targetEntity = null;
        if (command.Request.TargetEntity is not null)
        {
            Result<TargetEntity, Error> targetEntityResult = TargetEntity.Of(
                command.Request.TargetEntity.Type,
                command.Request.TargetEntity.Id);
            if (targetEntityResult.IsFailure)
            {
                return targetEntityResult.Error;
            }

            if (!_targetEntityOptions.AllowedTargetEntityTypes.Contains(
                    targetEntityResult.Value.Type,
                    StringComparer.OrdinalIgnoreCase))
            {
                return GeneralErrors.ValueIsInvalid("targetEntity.type");
            }

            targetEntity = targetEntityResult.Value;
        }

        UnitResult<Error> targetRuleValidation = AssetTargetRules.Validate(usageType, targetEntity);
        if (targetRuleValidation.IsFailure)
        {
            return targetRuleValidation.Error;
        }

        if (targetEntity is not null)
        {
            UnitResult<Error> targetAuthorization =
                await _targetAuthorization.AuthorizeAsync(targetEntity, cancellationToken);
            if (targetAuthorization.IsFailure)
                return targetAuthorization.Error;
        }

        string requestedExternalId = NormalizeExternalAssetId(command.Request.ExternalVideoId);
        Result<VideoProviderRef, Error> existingProviderRef =
            await _videoProviderRefRepository.GetByAsync(
                providerRef => providerRef.ExternalAssetId == requestedExternalId,
                cancellationToken);
        if (existingProviderRef.IsSuccess &&
            !await IsReclaimableAsync(existingProviderRef.Value, cancellationToken))
        {
            return Error.Conflict(
                "video.external.already.attached",
                "Это внешнее видео уже прикреплено");
        }

        Result<VideoProviderAssetInfo, Error> statusResult =
            await _videoProvider.GetStatusAsync(command.Request.ExternalVideoId, cancellationToken);
        if (statusResult.IsFailure)
        {
            return statusResult.Error;
        }

        VideoProviderAssetInfo providerInfo = statusResult.Value;
        string canonicalExternalId = NormalizeExternalAssetId(providerInfo.ExternalAssetId);
        if (string.IsNullOrWhiteSpace(canonicalExternalId))
            return Error.Failure("video.provider.id.empty", "Провайдер вернул пустой идентификатор видео");

        AssetStatus initialStatus = KinescopeStatusMapper.MapToAssetStatus(providerInfo.Status);

        if (initialStatus is AssetStatus.FAILED)
        {
            return Error.Validation("video.failed", "Внешнее видео находится в состоянии ошибки");
        }

        if (initialStatus is AssetStatus.PENDING_UPLOAD)
        {
            initialStatus = AssetStatus.PROCESSING;
        }

        Result<FileName, Error> fileNameResult = FileName.Of(
            string.IsNullOrWhiteSpace(providerInfo.Title) ? "external-video.mp4" : $"{providerInfo.Title}.mp4");
        if (fileNameResult.IsFailure)
        {
            return fileNameResult.Error;
        }

        Result<MediaContentType, Error> contentTypeResult = MediaContentType.Of("video/mp4");
        if (contentTypeResult.IsFailure)
        {
            return contentTypeResult.Error;
        }

        UnitResult<Error> beginTransaction =
            await _transactionManager.BeginTransactionAsync(cancellationToken);
        if (beginTransaction.IsFailure)
            return beginTransaction.Error;

        await _videoProviderRefRepository.AcquireExternalAssetLockAsync(
            canonicalExternalId,
            cancellationToken);

        existingProviderRef = await _videoProviderRefRepository.GetByAsync(
            providerRef => providerRef.ExternalAssetId == canonicalExternalId,
            cancellationToken);
        if (existingProviderRef.IsSuccess)
        {
            if (!await IsReclaimableAsync(existingProviderRef.Value, cancellationToken))
            {
                return Error.Conflict(
                    "video.external.already.attached",
                    "Это внешнее видео уже прикреплено");
            }

            await _videoProviderRefRepository.DeleteAsync(existingProviderRef.Value, cancellationToken);
        }

        Result<MediaAsset, Error> assetResult = MediaAsset.RegisterFromProvider(
            Guid.CreateVersion7(),
            usageType,
            fileNameResult.Value,
            contentTypeResult.Value,
            targetEntity,
            initialStatus,
            command.Request.DraftId,
            _user.UserId);

        if (assetResult.IsFailure)
        {
            return assetResult.Error;
        }

        MediaAsset asset = assetResult.Value;

        Result<VideoProviderRef, Error> providerRefResult = VideoProviderRef.Create(
            asset.Id,
            AssetProviderType.KINESCOPE,
            canonicalExternalId);

        if (providerRefResult.IsFailure)
        {
            return providerRefResult.Error;
        }

        providerRefResult.Value.UpdateMetadata(new VideoProviderMetadata
        {
            ThumbnailUrl = providerInfo.ThumbnailUrl,
            DurationSeconds = providerInfo.Duration,
        });

        await _assetRepository.AddAsync(asset, cancellationToken);
        await _videoProviderRefRepository.AddAsync(providerRefResult.Value, cancellationToken);

        // Draft mode defers cross-service events until BindDraftAssets runs.
        if (targetEntity is not null)
        {
            if (initialStatus == AssetStatus.READY)
            {
                Result<long, Error> binding = await _bindEventPublisher.PublishAsync(
                    asset,
                    targetEntity,
                    advanceBindingRevision: AssetUsagePolicyCatalog.IsSingleAssetPerEntity(asset.UsageType),
                    cancellationToken: cancellationToken);
                if (binding.IsFailure)
                    return binding.Error;
            }
            else
            {
                await _outbox.PublishAsync(new VideoUploadInitiated(
                    asset.Id,
                    asset.Kind.ToString().ToLowerInvariant(),
                    asset.UsageType.ToApiString(),
                    targetEntity.Id,
                    targetEntity.Type));
            }
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        UnitResult<Error> commitResult = await _transactionManager.CommitTransactionAsync(cancellationToken);
        if (commitResult.IsFailure)
            return commitResult.Error;

        return new AttachExistingVideoResponse(
            asset.Id,
            asset.Status.ToApiString(),
            providerInfo.ThumbnailUrl,
            providerInfo.Duration);
    }

    private async Task<bool> IsReclaimableAsync(
        VideoProviderRef providerRef,
        CancellationToken cancellationToken)
    {
        Result<MediaAsset, Error> assetResult = await _assetRepository.GetByAsync(
            asset => asset.Id == providerRef.AssetId,
            cancellationToken);

        return assetResult.IsSuccess &&
               assetResult.Value.Status is AssetStatus.DELETING or AssetStatus.DELETED;
    }

    private static string NormalizeExternalAssetId(string value)
    {
        string trimmed = value.Trim();
        return Guid.TryParse(trimmed, out Guid parsed)
            ? parsed.ToString("D")
            : trimmed;
    }
}