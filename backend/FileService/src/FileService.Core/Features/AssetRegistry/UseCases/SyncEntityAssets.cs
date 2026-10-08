using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FileService.Contracts.Assets;
using FileService.Core.Repositories;
using FileService.Core.Services.AssetRegistry;
using FileService.Domain;
using FluentValidation;
using FluentValidation.Results;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth.Authorization;
using PlatformAuth.Middleware;

namespace FileService.Core.Features.AssetRegistry.UseCases;

public sealed record SyncEntityAssetsCommand(SyncEntityAssetsRequest Request) : ICommand;

public sealed class SyncEntityAssetsValidator : AbstractValidator<SyncEntityAssetsCommand>
{
    public SyncEntityAssetsValidator()
    {
        RuleFor(x => x.Request.TargetEntity.Id)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("targetEntity.id"));

        RuleFor(x => x.Request.TargetEntity.Type)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("targetEntity.type"));

        RuleFor(x => x.Request.UsageTypes)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("usageTypes"));

        RuleForEach(x => x.Request.UsageTypes)
            .MustBeValueObject(usageType => AssetUsageTypeExtensions.FromString(usageType));

        RuleFor(x => x.Request.ActiveAssetIds)
            .NotNull()
            .WithError(GeneralErrors.ValueIsRequired("activeAssetIds"));
    }
}

public sealed class SyncEntityAssetsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/assets/sync", async Task<EndpointResult<int>> (
                [FromBody] SyncEntityAssetsRequest request,
                [FromServices] ICommandHandler<int, SyncEntityAssetsCommand> handler,
                CancellationToken token) => await handler.Handle(new SyncEntityAssetsCommand(request), token))
            .RequirePermissions(PlatformPermissions.Files.MANAGE);
    }
}

public sealed class SyncEntityAssetsHandler : ICommandHandler<int, SyncEntityAssetsCommand>
{
    private readonly IMediaAssetRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<SyncEntityAssetsCommand> _validator;
    private readonly ITargetEntityAuthorization _targetAuthorization;

    public SyncEntityAssetsHandler(
        IMediaAssetRepository repository,
        ITransactionManager transactionManager,
        IValidator<SyncEntityAssetsCommand> validator,
        ITargetEntityAuthorization targetAuthorization)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _validator = validator;
        _targetAuthorization = targetAuthorization;
    }

    public async Task<Result<int, Error>> Handle(SyncEntityAssetsCommand command, CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return validation.ToError();

        SyncEntityAssetsRequest request = command.Request;

        Result<TargetEntity, Error> targetEntityResult = TargetEntity.Of(request.TargetEntity.Type, request.TargetEntity.Id);
        if (targetEntityResult.IsFailure)
            return targetEntityResult.Error;

        List<AssetUsageType> usageTypes = [];
        foreach (string usageTypeStr in request.UsageTypes)
        {
            Result<AssetUsageType, Error> parseResult = AssetUsageTypeExtensions.FromString(usageTypeStr);
            if (parseResult.IsFailure)
                return parseResult.Error;

            if (AssetUsagePolicyCatalog.IsSingleAssetPerEntity(parseResult.Value))
            {
                return Error.Validation(
                    "asset.sync.single_slot.unsupported",
                    "Одиночные media-ресурсы изменяются только через authoritative aggregate");
            }

            usageTypes.Add(parseResult.Value);
        }

        TargetEntity target = targetEntityResult.Value;
        UnitResult<Error> targetAuthorization =
            await _targetAuthorization.AuthorizeAsync(target, cancellationToken);
        if (targetAuthorization.IsFailure)
            return targetAuthorization.Error;

        HashSet<Guid> activeIds = request.ActiveAssetIds.ToHashSet();

        List<MediaAsset> boundAssets = await _repository.GetManyByAsync(
            a => a.TargetEntity != null
                 && a.TargetEntity.Type == target.Type
                 && a.TargetEntity.Id == target.Id
                 && usageTypes.Contains(a.UsageType)
                 && a.Status == AssetStatus.READY,
            cancellationToken);

        int orphanCount = 0;
        foreach (MediaAsset asset in boundAssets.Where(a => !activeIds.Contains(a.Id)))
        {
            UnitResult<Error> deleteResult = asset.RequestDelete();
            if (deleteResult.IsSuccess)
                orphanCount++;
        }

        if (orphanCount > 0)
        {
            UnitResult<Error> saveResult = await _transactionManager.SaveChangesAsync(cancellationToken);
            if (saveResult.IsFailure)
                return saveResult.Error;
        }

        return orphanCount;
    }
}
