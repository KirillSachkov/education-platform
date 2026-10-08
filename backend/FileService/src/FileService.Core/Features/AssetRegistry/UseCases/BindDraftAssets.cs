using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FileService.Contracts.Assets;
using FileService.Core.Database;
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
using Shared.Messaging.IntegrationEvents.Files.Events;

namespace FileService.Core.Features.AssetRegistry.UseCases;

public sealed record BindDraftAssetsCommand(BindDraftAssetsRequest Request) : ICommand;

public sealed class BindDraftAssetsRequestValidator : AbstractValidator<BindDraftAssetsCommand>
{
    public BindDraftAssetsRequestValidator()
    {
        RuleFor(x => x.Request.DraftId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("draftId"));

        RuleFor(x => x.Request.TargetEntity.Id)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("targetEntity.id"));

        RuleFor(x => x.Request.TargetEntity.Type)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("targetEntity.type"));

        RuleFor(x => x.Request.AssetIds)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired("assetIds"));
    }
}

public sealed class BindDraftAssetsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/draft-assets/bind", async Task<EndpointResult> (
                [FromBody] BindDraftAssetsRequest request,
                [FromServices] ICommandHandler<BindDraftAssetsCommand> handler,
                CancellationToken token) => await handler.Handle(new BindDraftAssetsCommand(request), token))
            .RequirePermissions(PlatformPermissions.Files.MANAGE);
    }
}

public sealed class BindDraftAssetsHandler : ICommandHandler<BindDraftAssetsCommand>
{
    private readonly IMediaAssetRepository _repository;
    private readonly AssetBindEventPublisher _bindEventPublisher;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<BindDraftAssetsCommand> _validator;
    private readonly TargetEntityOptions _targetEntityOptions;
    private readonly ITargetEntityAuthorization _targetAuthorization;
    private readonly UserScopedData _user;

    public BindDraftAssetsHandler(
        IMediaAssetRepository repository,
        AssetBindEventPublisher bindEventPublisher,
        ITransactionManager transactionManager,
        IValidator<BindDraftAssetsCommand> validator,
        IOptions<TargetEntityOptions> targetEntityOptions,
        ITargetEntityAuthorization targetAuthorization,
        UserScopedData user)
    {
        _repository = repository;
        _bindEventPublisher = bindEventPublisher;
        _transactionManager = transactionManager;
        _validator = validator;
        _targetEntityOptions = targetEntityOptions.Value;
        _targetAuthorization = targetAuthorization;
        _user = user;
    }

    public async Task<UnitResult<Error>> Handle(BindDraftAssetsCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validationResult = await _validator.ValidateAsync(command, cancellationToken);
        if (!validationResult.IsValid)
            return validationResult.ToError();

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

        UnitResult<Error> targetAuthorization = await _targetAuthorization.AuthorizeAsync(
            targetEntityResult.Value,
            cancellationToken);
        if (targetAuthorization.IsFailure)
            return targetAuthorization.Error;

        List<MediaAsset> draftAssets = await _repository.GetByDraftIdAsync(command.Request.DraftId, cancellationToken);

        bool isPrivileged = _user.IsAdmin;
        // Two ownership gates — both must pass:
        //   1. Non-empty: caller must have actually uploaded to this draft. An empty
        //      result set means either the draft never existed or belongs to a different
        //      user — treat both as "not yours" to avoid leaking draft existence.
        //   2. All-owned: every asset under this draft must belong to the caller. A
        //      mixed-ownership draft would indicate a DB integrity issue, not a valid
        //      state we should cooperate with.
        if (!isPrivileged
            && (draftAssets.Count == 0 || draftAssets.Any(a => a.UploadedByUserId != _user.UserId)))
        {
            return Error.Authorization(
                "draft.not.owner",
                "Черновик не найден или принадлежит другому пользователю");
        }

        HashSet<Guid> requestedIds = command.Request.AssetIds.ToHashSet();
        HashSet<Guid> draftIds = draftAssets.Select(x => x.Id).ToHashSet();

        if (requestedIds.Count > 0 && !requestedIds.IsSubsetOf(draftIds))
        {
            return Error.Validation("asset.draft.mismatch", "Один или несколько ресурсов не принадлежат черновику");
        }

        List<MediaAsset> requestedAssets = draftAssets
            .Where(asset => requestedIds.Contains(asset.Id))
            .ToList();

        if (requestedAssets.Any(asset => AssetUsagePolicyCatalog.IsSingleAssetPerEntity(asset.UsageType)))
        {
            return Error.Validation(
                "asset.bind.single_slot.unsupported",
                "Одиночные media-ресурсы привязываются только через authoritative aggregate");
        }

        // Files must be Ready before bind. Videos (Kinescope) may still be
        // Processing at save time — BindTo() allows that, and reconciliation
        // will later re-publish FileAssetBound when it transitions to Ready.
        foreach (MediaAsset asset in requestedAssets)
        {
            bool statusOk = asset.Kind == AssetKind.VIDEO
                ? asset.Status is AssetStatus.READY or AssetStatus.PROCESSING
                : asset.Status == AssetStatus.READY;

            if (!statusOk)
            {
                return Error.Validation("asset.not.ready", "Один или несколько ресурсов ещё не готовы к привязке");
            }
        }

        foreach (MediaAsset asset in requestedAssets)
        {
            UnitResult<Error> bindResult = asset.BindTo(targetEntityResult.Value);
            if (bindResult.IsFailure)
            {
                return bindResult.Error;
            }

            Result<long, Error> binding = await _bindEventPublisher.PublishAsync(
                asset,
                targetEntityResult.Value,
                advanceBindingRevision: true,
                cancellationToken: cancellationToken);
            if (binding.IsFailure)
                return binding.Error;
        }

        // Orphan drafts never had a TargetEntity; no FileAssetDeleted event
        // is required (no downstream service knew about them). Still check
        // RequestDelete() result to avoid silent skips on unexpected state.
        foreach (MediaAsset orphan in draftAssets.Where(a => !requestedIds.Contains(a.Id)))
        {
            orphan.RequestDelete();
        }

        return await _transactionManager.SaveChangesAsync(cancellationToken);
    }

}
