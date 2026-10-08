using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FileService.Contracts.Assets;
using FileService.Core.Database;
using FileService.Core.Messaging;
using FileService.Core.Repositories;
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

namespace FileService.Core.Features.AssetRegistry.UseCases;

public sealed record BindAssetCommand(
    Guid AssetId,
    BindAssetRequest Request,
    Guid? ActorUserId = null,
    bool TrustedTarget = false,
    bool ActorCanManageAnyAsset = false) : ICommand;

public sealed class BindAssetCommandValidator : AbstractValidator<BindAssetCommand>
{
    public BindAssetCommandValidator()
    {
        RuleFor(x => x.AssetId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("assetId"));

        RuleFor(x => x.Request.TargetEntity.Id)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("targetEntity.id"));

        RuleFor(x => x.Request.TargetEntity.Type)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("targetEntity.type"));

        RuleFor(x => x.ActorUserId)
            .NotEmpty()
            .When(x => x.TrustedTarget && !x.ActorCanManageAnyAsset)
            .WithError(GeneralErrors.ValueIsRequired("actorUserId"));
    }
}

public sealed class BindAssetInternalEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/assets/{assetId:guid}/bind/", async Task<EndpointResult<BindAssetResponse>> (
                [FromRoute] Guid assetId,
                [FromBody] BindAssetInternalRequest request,
                [FromServices] ICommandHandler<BindAssetResponse, BindAssetCommand> handler,
                CancellationToken token) => await handler.Handle(
                new BindAssetCommand(
                    assetId,
                    new BindAssetRequest(request.TargetEntity, request.SelectionId),
                    request.ActorUserId,
                    TrustedTarget: true,
                    request.ActorCanManageAnyAsset),
                token))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class BindAssetEndpoint : IEndpoint
{
    public const string ASSET_BIND_RATE_LIMIT_POLICY = "asset-bind";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/files/{assetId:guid}/bind", async Task<EndpointResult<BindAssetResponse>> (
                [FromRoute] Guid assetId,
                [FromBody] BindAssetRequest request,
                [FromServices] ICommandHandler<BindAssetResponse, BindAssetCommand> handler,
                CancellationToken token) => await handler.Handle(new BindAssetCommand(assetId, request), token))
            .RequirePermissions(PlatformPermissions.Files.MANAGE)
            .RequireRateLimiting(ASSET_BIND_RATE_LIMIT_POLICY);
    }
}

/// <summary>
///     Sync-эндпоинт для подготовки привязки одного ассета к целевой сущности.
///     Для single-slot media новая логическая selection получает монотонную revision;
///     конкурентные повторы одной неподтверждённой selection переиспользуют revision.
///     Authoritative сервис сохраняет её вместе с aggregate и только после commit
///     публикует confirmation и удаление предыдущей revision.
/// </summary>
public sealed class BindAssetHandler : ICommandHandler<BindAssetResponse, BindAssetCommand>
{
    private readonly IMediaAssetRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<BindAssetCommand> _validator;
    private readonly TargetEntityOptions _targetEntityOptions;
    private readonly IOutboxService _outbox;
    private readonly AssetBindEventPublisher _bindEventPublisher;
    private readonly ITargetEntityAuthorization _targetAuthorization;
    private readonly UserScopedData _user;

    public BindAssetHandler(
        IMediaAssetRepository repository,
        ITransactionManager transactionManager,
        IValidator<BindAssetCommand> validator,
        IOptions<TargetEntityOptions> targetEntityOptions,
        IOutboxService outbox,
        AssetBindEventPublisher bindEventPublisher,
        ITargetEntityAuthorization targetAuthorization,
        UserScopedData user)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _validator = validator;
        _targetEntityOptions = targetEntityOptions.Value;
        _outbox = outbox;
        _bindEventPublisher = bindEventPublisher;
        _targetAuthorization = targetAuthorization;
        _user = user;
    }

    public async Task<Result<BindAssetResponse, Error>> Handle(
        BindAssetCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

        Result<TargetEntity, Error> targetEntityResult = TargetEntity.Of(
            command.Request.TargetEntity.Type,
            command.Request.TargetEntity.Id);
        if (targetEntityResult.IsFailure)
            return targetEntityResult.Error;

        if (!_targetEntityOptions.AllowedTargetEntityTypes.Contains(
                targetEntityResult.Value.Type, StringComparer.OrdinalIgnoreCase))
            return GeneralErrors.ValueIsInvalid("targetEntity.type");

        if (!command.TrustedTarget)
        {
            UnitResult<Error> targetAuthorization = await _targetAuthorization.AuthorizeAsync(
                targetEntityResult.Value,
                cancellationToken: cancellationToken);
            if (targetAuthorization.IsFailure)
                return targetAuthorization.Error;
        }

        Result<MediaAsset, Error> assetResult = await _repository.GetByAsync(
            a => a.Id == command.AssetId, cancellationToken);
        if (assetResult.IsFailure)
            return assetResult.Error;

        MediaAsset asset = assetResult.Value;
        bool isSingleSlot = AssetUsagePolicyCatalog.IsSingleAssetPerEntity(asset.UsageType);

        Guid? actorUserId = command.TrustedTarget ? command.ActorUserId : _user.UserId;
        bool isPrivileged = command.TrustedTarget ? command.ActorCanManageAnyAsset : _user.IsAdmin;
        if (!isPrivileged && asset.UploadedByUserId != actorUserId)
            return Error.Authorization("asset.bind.not.owner", "Нет доступа к данному ресурсу");

        bool statusAllowsBinding = asset.Kind == AssetKind.VIDEO
            ? asset.Status is AssetStatus.PROCESSING or AssetStatus.READY
            : asset.Status == AssetStatus.READY;
        if (!statusAllowsBinding)
            return Error.Validation("asset.not.ready", "Привязать можно только готовые ресурсы");

        if (asset.TargetEntity is not null
            && string.Equals(asset.TargetEntity.Type, targetEntityResult.Value.Type, StringComparison.Ordinal)
            && asset.TargetEntity.Id == targetEntityResult.Value.Id)
        {
            // Direct author calls are legacy-compatible for the first bind, but
            // only the authoritative S2S path may create a new same-target
            // revision. Otherwise a caller can strand arbitrary prepared revisions
            // that no aggregate will ever confirm.
            bool canAdvanceSameTarget = command.TrustedTarget || _user.IsAdmin ||
                                        _user.HasRole(PlatformRoles.SERVICE);
            if (isSingleSlot && !canAdvanceSameTarget)
                return new BindAssetResponse(asset.BindingRevision);

            if (command.Request.SelectionId is { } selectionId &&
                asset.BindingSelectionId == selectionId)
            {
                return new BindAssetResponse(asset.BindingRevision);
            }

            Result<long, Error> republish = await _bindEventPublisher.PublishAsync(
                asset,
                targetEntityResult.Value,
                advanceBindingRevision: isSingleSlot,
                requiresAuthoritativeConfirmation: isSingleSlot,
                selectionId: command.Request.SelectionId,
                cancellationToken: cancellationToken);
            if (republish.IsFailure)
                return republish.Error;

            UnitResult<Error> republishSave = await _transactionManager.SaveChangesAsync(cancellationToken);
            return republishSave.IsFailure
                ? republishSave.Error
                : new BindAssetResponse(republish.Value);
        }

        // Already bound to a different target — caller must detach first.
        if (asset.TargetEntity is not null)
        {
            return Error.Conflict("asset.already.bound", "Ресурс уже привязан к другой сущности");
        }

        UnitResult<Error> bindResult = asset.BindTo(targetEntityResult.Value);
        if (bindResult.IsFailure)
            return bindResult.Error;

        // #646: generate responsive variants on bind only when not already done.
        // CompleteFileUpload publishes generation on Ready, so on the normal upload→bind
        // path variants usually exist by now — re-publishing would double the S3 download
        // + CPU. Generation is idempotent, so this is a waste-avoidance guard, not a
        // correctness one (a bind of an asset that completed before #646, or whose
        // generation hasn't run yet, still gets variants here).
        if (asset.IsImageVariantEligible() && asset.ImageVariants.Count == 0)
        {
            await _outbox.PublishAsync(new GenerateImageVariants(asset.Id));
        }

        Result<long, Error> binding = await _bindEventPublisher.PublishAsync(
            asset,
            targetEntityResult.Value,
            advanceBindingRevision: true,
            requiresAuthoritativeConfirmation: isSingleSlot,
            selectionId: command.Request.SelectionId,
            cancellationToken: cancellationToken);
        if (binding.IsFailure)
            return binding.Error;

        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(cancellationToken);
        return save.IsFailure
            ? save.Error
            : new BindAssetResponse(binding.Value);
    }
}
