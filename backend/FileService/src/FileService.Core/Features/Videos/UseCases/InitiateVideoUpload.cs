using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FileService.Contracts.Assets;
using FileService.Core.Database;
using FileService.Core.Features.AssetRegistry;
using FileService.Core.Features.Files.UseCases;
using FileService.Core.Repositories;
using FileService.Core.Services;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Features.Videos.UseCases;

public sealed record InitiateVideoUploadCommand(InitiateVideoUploadRequest Request, string? ClientIp) : ICommand;

public sealed class InitiateVideoUploadEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/videos/uploads", async Task<EndpointResult<InitiateVideoUploadResponse>> (
                    [FromBody] InitiateVideoUploadRequest request,
                    HttpContext httpContext,
                    [FromServices] ICommandHandler<InitiateVideoUploadResponse, InitiateVideoUploadCommand> handler,
                    CancellationToken token) =>
                await handler.Handle(
                    new InitiateVideoUploadCommand(request, httpContext.Connection.RemoteIpAddress?.ToString()), token))
            .RequirePermissions(PlatformPermissions.Videos.MANAGE)
            // Делит лимит "file-upload" (30/мин на юзера) с file-uploads — раньше video-upload
            // был БЕЗ rate-limit'а вообще (cost-амплификация на Kinescope upload-сессиях).
            .RequireRateLimiting(InitiateFileUploadEndpoint.FILE_UPLOAD_RATE_LIMIT_POLICY);
    }
}

public sealed class InitiateVideoUploadRequestValidator : AbstractValidator<InitiateVideoUploadRequest>
{
    public InitiateVideoUploadRequestValidator()
    {
        RuleFor(x => x.FileName).NotEmpty().WithError(GeneralErrors.ValueIsRequired("fileName"));
        RuleFor(x => x.ContentType)
            .NotEmpty().WithError(GeneralErrors.ValueIsRequired("contentType"))
            .Must(x => x.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            .WithError(Error.Validation("contentType.invalid", "Тип контента должен быть video/*"));
        RuleFor(x => x.Size).GreaterThan(0).WithError(GeneralErrors.ValueIsInvalid("size"));
        RuleFor(x => x.UsageType)
            .NotEmpty().WithError(GeneralErrors.ValueIsRequired("usageType"))
            .MustBeValueObject(x => AssetUsageTypeExtensions.FromString(x, "usageType"));
        RuleFor(x => x)
            .Must(x => x.TargetEntity is not null || x.DraftId.HasValue)
            .WithError(GeneralErrors.ValueIsRequired("targetEntity"));
    }
}

public sealed class
    InitiateVideoUploadHandler : ICommandHandler<InitiateVideoUploadResponse, InitiateVideoUploadCommand>
{
    private readonly IVideoProvider _videoProvider;
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IVideoProviderRefRepository _videoProviderRefRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IOutboxService _outbox;
    private readonly IValidator<InitiateVideoUploadRequest> _validator;
    private readonly TargetEntityOptions _targetEntityOptions;
    private readonly ITargetEntityAuthorization _targetAuthorization;
    private readonly UserScopedData _user;
    private readonly ILogger<InitiateVideoUploadHandler> _logger;

    public InitiateVideoUploadHandler(
        IVideoProvider videoProvider,
        IMediaAssetRepository assetRepository,
        IVideoProviderRefRepository videoProviderRefRepository,
        ITransactionManager transactionManager,
        IOutboxService outbox,
        IValidator<InitiateVideoUploadRequest> validator,
        IOptions<TargetEntityOptions> targetEntityOptions,
        ITargetEntityAuthorization targetAuthorization,
        UserScopedData user,
        ILogger<InitiateVideoUploadHandler> logger)
    {
        _videoProvider = videoProvider;
        _assetRepository = assetRepository;
        _videoProviderRefRepository = videoProviderRefRepository;
        _transactionManager = transactionManager;
        _outbox = outbox;
        _validator = validator;
        _targetEntityOptions = targetEntityOptions.Value;
        _targetAuthorization = targetAuthorization;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<InitiateVideoUploadResponse, Error>> Handle(
        InitiateVideoUploadCommand command,
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

        AssetUsagePolicy policy = policyResult.Value;

        if (policy.Kind != AssetKind.VIDEO)
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

        UnitResult<Error> targetRuleValidation =
            AssetTargetRules.Validate(usageType, targetEntity);
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

        Result<FileName, Error> fileNameResult = FileName.Of(command.Request.FileName);
        if (fileNameResult.IsFailure)
        {
            return fileNameResult.Error;
        }

        Result<MediaContentType, Error> contentTypeResult = MediaContentType.Of(command.Request.ContentType);
        if (contentTypeResult.IsFailure)
        {
            return contentTypeResult.Error;
        }

        UnitResult<Error> uploadValidation = policy.ValidateUpload(
            fileNameResult.Value,
            contentTypeResult.Value,
            command.Request.Size);
        if (uploadValidation.IsFailure)
        {
            return uploadValidation.Error;
        }

        Result<VideoUploadInitResult, Error> uploadResult = await _videoProvider.InitiateUploadAsync(
            Path.GetFileNameWithoutExtension(fileNameResult.Value.Value),
            fileNameResult.Value.Value,
            command.Request.Size,
            command.ClientIp,
            cancellationToken);

        if (uploadResult.IsFailure)
        {
            return uploadResult.Error;
        }

        bool isTemporary = policy.RegistrationMode == AssetRegistrationMode.DraftOrEntity
            ? command.Request.DraftId.HasValue
            : policy.RequiresDraft;

        Result<MediaAsset, Error> assetResult = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.VIDEO,
            usageType,
            fileNameResult.Value,
            contentTypeResult.Value,
            command.Request.Size,
            command.Request.DraftId,
            targetEntity,
            isTemporary,
            _user.UserId);

        if (assetResult.IsFailure)
        {
            return assetResult.Error;
        }

        MediaAsset asset = assetResult.Value;
        UnitResult<Error> processingResult = asset.MarkProcessing();
        if (processingResult.IsFailure)
            return processingResult.Error;

        Result<VideoProviderRef, Error> providerRefResult = VideoProviderRef.Create(
            asset.Id,
            AssetProviderType.KINESCOPE,
            uploadResult.Value.ExternalAssetId);

        if (providerRefResult.IsFailure)
        {
            return providerRefResult.Error;
        }

        await _assetRepository.AddAsync(asset, cancellationToken);
        await _videoProviderRefRepository.AddAsync(providerRefResult.Value, cancellationToken);

        // Draft uploads delay VideoUploadInitiated until BindDraftAssets — only
        // bound videos reach the EducationContentService handlers. The video
        // provider reconciliation will pick the asset up regardless.
        if (targetEntity is not null)
        {
            await _outbox.PublishAsync(new VideoUploadInitiated(
                asset.Id,
                asset.Kind.ToString().ToLowerInvariant(),
                asset.UsageType.ToApiString(),
                targetEntity.Id,
                targetEntity.Type));
        }

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "Video upload initiated: AssetId={AssetId}, UsageType={UsageType}",
            asset.Id, asset.UsageType);

        return new InitiateVideoUploadResponse(
            asset.Id,
            asset.Status.ToApiString(),
            uploadResult.Value.UploadUrl,
            uploadResult.Value.ExternalAssetId);
    }
}
