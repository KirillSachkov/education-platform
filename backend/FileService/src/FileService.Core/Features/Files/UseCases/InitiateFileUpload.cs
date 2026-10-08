using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FileService.Contracts.Assets;
using FileService.Core.Features.AssetRegistry;
using FileService.Core.FilesStorage;
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

namespace FileService.Core.Features.Files.UseCases;

public sealed record InitiateFileUploadCommand(InitiateFileUploadRequest Request) : ICommand;

public sealed class InitiateFileUploadEndpoint : IEndpoint
{
    /// <summary>
    /// Имя rate-limit-политики для POST /files/uploads.
    /// Реализация — в FileService.Web/Configuration/FileRateLimiting.cs.
    /// Дублируется как const, чтобы Core не зависел от Web-слоя.
    /// </summary>
    public const string FILE_UPLOAD_RATE_LIMIT_POLICY = "file-upload";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/files/uploads", async Task<EndpointResult<InitiateFileUploadResponse>> (
                [FromBody] InitiateFileUploadRequest request,
                [FromServices] ICommandHandler<InitiateFileUploadResponse, InitiateFileUploadCommand> handler,
                CancellationToken token) => await handler.Handle(new InitiateFileUploadCommand(request), token))
            .RequirePermissions(PlatformPermissions.Files.UPLOAD)
            .RequireRateLimiting(FILE_UPLOAD_RATE_LIMIT_POLICY);
    }
}

public sealed class InitiateFileUploadRequestValidator : AbstractValidator<InitiateFileUploadRequest>
{
    public InitiateFileUploadRequestValidator()
    {
        RuleFor(x => x.FileName)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("fileName"));

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("contentType"));

        RuleFor(x => x.Size)
            .GreaterThan(0)
            .WithError(GeneralErrors.ValueIsInvalid("size"));

        RuleFor(x => x.UsageType)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("usageType"))
            .MustBeValueObject(usageType => AssetUsageTypeExtensions.FromString(usageType));
    }
}

public sealed class InitiateFileUploadHandler
    : ICommandHandler<InitiateFileUploadResponse, InitiateFileUploadCommand>
{
    private readonly IObjectStorageProvider _objectStorageProvider;
    private readonly IMediaAssetRepository _assetRepository;
    private readonly IFileStorageRefRepository _fileStorageRefRepository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<InitiateFileUploadRequest> _validator;
    private readonly TargetEntityOptions _targetEntityOptions;
    private readonly FileContentUrlBuilder _contentUrlBuilder;
    private readonly ITargetEntityAuthorization _targetAuthorization;
    private readonly UserScopedData _user;
    private readonly ILogger<InitiateFileUploadHandler> _logger;

    public InitiateFileUploadHandler(
        IObjectStorageProvider objectStorageProvider,
        IMediaAssetRepository assetRepository,
        IFileStorageRefRepository fileStorageRefRepository,
        ITransactionManager transactionManager,
        IValidator<InitiateFileUploadRequest> validator,
        IOptions<TargetEntityOptions> targetEntityOptions,
        FileContentUrlBuilder contentUrlBuilder,
        ITargetEntityAuthorization targetAuthorization,
        UserScopedData user,
        ILogger<InitiateFileUploadHandler> logger)
    {
        _objectStorageProvider = objectStorageProvider;
        _assetRepository = assetRepository;
        _fileStorageRefRepository = fileStorageRefRepository;
        _transactionManager = transactionManager;
        _validator = validator;
        _targetEntityOptions = targetEntityOptions.Value;
        _contentUrlBuilder = contentUrlBuilder;
        _targetAuthorization = targetAuthorization;
        _user = user;
        _logger = logger;
    }

    public async Task<Result<InitiateFileUploadResponse, Error>> Handle(
        InitiateFileUploadCommand command,
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
        if (policy.Kind != AssetKind.FILE)
        {
            return Error.Validation("usageType.invalid", "Тип использования должен быть файловым");
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

        TargetEntity? targetEntity = null;
        if (command.Request.TargetEntity is not null)
        {
            Result<TargetEntity, Error> targetResult = TargetEntity.Of(
                command.Request.TargetEntity.Type,
                command.Request.TargetEntity.Id);

            if (targetResult.IsFailure)
            {
                return targetResult.Error;
            }

            if (!_targetEntityOptions.AllowedTargetEntityTypes.Contains(
                    targetResult.Value.Type,
                    StringComparer.OrdinalIgnoreCase))
            {
                return GeneralErrors.ValueIsInvalid("targetEntity.type");
            }

            targetEntity = targetResult.Value;
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

        if (usageType == AssetUsageType.AVATAR &&
            targetEntity is not null &&
            !_user.IsOwnerOrAdmin(targetEntity.Id))
        {
            return Error.Authorization(
                "avatar.target.not.owner",
                "Нельзя загрузить аватар для другого пользователя");
        }

        Result<string, Error> canonicalExtensionResult = policy.GetCanonicalExtension(contentTypeResult.Value);
        if (canonicalExtensionResult.IsFailure)
        {
            return canonicalExtensionResult.Error;
        }

        bool isTemporary = policy.RegistrationMode == AssetRegistrationMode.DraftOrEntity
            ? command.Request.DraftId.HasValue
            : policy.RequiresDraft;

        Result<MediaAsset, Error> assetResult = MediaAsset.Register(
            Guid.CreateVersion7(),
            AssetKind.FILE,
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

        Result<StorageKey, Error> storageKeyResult = StorageKey.ForFile(asset.Id, canonicalExtensionResult.Value);
        if (storageKeyResult.IsFailure)
        {
            return storageKeyResult.Error;
        }

        Result<FileStorageRef, Error> fileStorageRefResult = FileStorageRef.Create(asset.Id, storageKeyResult.Value);
        if (fileStorageRefResult.IsFailure)
        {
            return fileStorageRefResult.Error;
        }

        FileStorageRef fileStorageRef = fileStorageRefResult.Value;

        Result<ObjectStorageUploadSession, Error> uploadSessionResult =
            await _objectStorageProvider.InitiateUploadAsync(
                fileStorageRef.StorageKey.Value,
                asset.ContentType.Value,
                asset.Size,
                cancellationToken);

        if (uploadSessionResult.IsFailure)
        {
            return uploadSessionResult.Error;
        }

        await _assetRepository.AddAsync(asset, cancellationToken);
        await _fileStorageRefRepository.AddAsync(fileStorageRef, cancellationToken);

        UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailure)
        {
            return saveResult.Error;
        }

        _logger.LogInformation(
            "File upload initiated: AssetId={AssetId}, UsageType={UsageType}",
            asset.Id, asset.UsageType);

        return new InitiateFileUploadResponse(
            asset.Id,
            asset.Status.ToApiString(),
            uploadSessionResult.Value.UploadUrl,
            uploadSessionResult.Value.RequiredHeaders,
            _contentUrlBuilder.Build(asset.Id));
    }
}
