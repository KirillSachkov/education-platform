using Core.Abstractions;
using Core.Database;
using Core.Validation;
using FileService.Contracts.Assets;
using FileService.Core.Repositories;
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

public sealed record ReassignAssetsOwnerCommand(ReassignAssetOwnerRequest Request) : ICommand;

public sealed class ReassignAssetsOwnerValidator : AbstractValidator<ReassignAssetsOwnerCommand>
{
    public ReassignAssetsOwnerValidator()
    {
        RuleFor(x => x.Request.NewOwnerId)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("newOwnerId"));

        RuleFor(x => x.Request.TargetEntities)
            .NotEmpty()
            .WithError(GeneralErrors.ValueIsRequired("targetEntities"));

        RuleForEach(x => x.Request.TargetEntities).ChildRules(target =>
        {
            target.RuleFor(x => x.Id)
                .NotEmpty()
                .WithError(GeneralErrors.ValueIsRequired("targetEntity.id"));

            target.RuleFor(x => x.Type)
                .NotEmpty()
                .WithError(GeneralErrors.ValueIsRequired("targetEntity.type"));
        });
    }
}

public sealed class ReassignAssetsOwnerEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/assets/reassign-owner", async Task<EndpointResult<int>> (
                [FromBody] ReassignAssetOwnerRequest request,
                [FromServices] ICommandHandler<int, ReassignAssetsOwnerCommand> handler,
                CancellationToken token) => await handler.Handle(new ReassignAssetsOwnerCommand(request), token))
            .RequireAnyRole(PlatformRoles.SERVICE, PlatformRoles.ADMIN);
    }
}

public sealed class ReassignAssetsOwnerHandler : ICommandHandler<int, ReassignAssetsOwnerCommand>
{
    private readonly IMediaAssetRepository _repository;
    private readonly ITransactionManager _transactionManager;
    private readonly IValidator<ReassignAssetsOwnerCommand> _validator;
    private readonly TargetEntityOptions _targetEntityOptions;

    public ReassignAssetsOwnerHandler(
        IMediaAssetRepository repository,
        ITransactionManager transactionManager,
        IValidator<ReassignAssetsOwnerCommand> validator,
        IOptions<TargetEntityOptions> targetEntityOptions)
    {
        _repository = repository;
        _transactionManager = transactionManager;
        _validator = validator;
        _targetEntityOptions = targetEntityOptions.Value;
    }

    public async Task<Result<int, Error>> Handle(
        ReassignAssetsOwnerCommand command,
        CancellationToken cancellationToken)
    {
        ValidationResult validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        List<TargetEntity> targets = [];
        foreach (var dto in command.Request.TargetEntities)
        {
            Result<TargetEntity, Error> targetResult = TargetEntity.Of(dto.Type, dto.Id);
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

            if (!targets.Contains(targetResult.Value))
            {
                targets.Add(targetResult.Value);
            }
        }

        List<MediaAsset> assets = await _repository.GetByTargetEntitiesAsync(targets, cancellationToken);

        int changed = 0;
        foreach (MediaAsset asset in assets)
        {
            if (asset.UploadedByUserId == command.Request.NewOwnerId)
            {
                continue;
            }

            UnitResult<Error> reassign = asset.ReassignOwner(command.Request.NewOwnerId);
            if (reassign.IsFailure)
            {
                return reassign.Error;
            }

            changed++;
        }

        if (changed == 0)
        {
            return 0;
        }

        UnitResult<Error> save = await _transactionManager.SaveChangesAsync(cancellationToken);
        return save.IsSuccess ? changed : save.Error;
    }
}
